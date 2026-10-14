using System.Text.RegularExpressions;
using Whisper.net;
using VoiceRecorder.Core;

namespace VoiceRecorder.Transcription;

/// <summary>Motor de transcripción basado en Whisper (whisper.cpp) local.</summary>
public sealed class WhisperTranscriber : ITranscriptionEngine, IDisposable
{
    private static readonly string[] HallucinationPhrases =
    [
        "gracias por ver",
        "gracias por vernos",
        "thank you for watching",
        "thanks for watching",
        "subtitles by",
        "amara.org",
        "subtitle",
        "suscrib",
        "activa la campana",
        "dale a like",
        "like y suscrib",
        "próximo vídeo",
        "siguiente vídeo",
        "next video",
        "see you in the next",
    ];

    /// <summary>Hueco máximo entre segmentos del mismo hablante para fusionarlos.</summary>
    private static readonly TimeSpan MergeGap = TimeSpan.FromSeconds(1);

    /// <summary>Anotaciones de sonidos no verbales que Whisper genera ([Música], (ruido)…).</summary>
    private const string SuppressPattern = @"\[[^\]]*\]|\([^)]*\)";

    private static readonly Regex AnnotationRegex = new(SuppressPattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex MultipleSpacesRegex = new(@"\s{2,}", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    private static readonly Regex RepeatedPunctuationRegex = new(@"([.,!?;:…])\1+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private readonly WhisperFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WhisperProcessor? _processor;
    private string? _processorLanguage;

    public WhisperTranscriber(string modelPath, string language = "es")
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("No se encontró el modelo Whisper.", modelPath);
        }

        _factory = WhisperFactory.FromPath(modelPath);
        Language = language;
        Threads = ComputeThreads(Environment.ProcessorCount);
    }

    /// <summary>
    /// Hilos de inferencia: todos los núcleos menos uno (mín. 3, máx. 16),
    /// dejando margen para la UI y la captura de audio.
    /// </summary>
    public static int ComputeThreads(int processorCount) => Math.Clamp(processorCount - 1, 3, 16);

    /// <summary>Idioma: "es", "en", ... o "auto" para detección.</summary>
    public string Language { get; set; } = "es";

    public int Threads { get; set; }

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(Utterance utterance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(utterance);
        if (utterance.Samples.Length == 0)
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var processor = GetProcessor();
            var results = new List<TranscriptSegment>();
            var maxEnd = utterance.Start + TimeSpan.FromSeconds(utterance.DurationSeconds);

            await foreach (var segment in processor.ProcessAsync(utterance.Samples, cancellationToken))
            {
                var text = CleanText(segment.Text ?? string.Empty);
                if (!HasContent(text) || IsHallucination(text, segment.NoSpeechProbability))
                {
                    continue;
                }

                var start = utterance.Start + segment.Start;
                var end = utterance.Start + segment.End;
                if (end > maxEnd)
                {
                    end = maxEnd;
                }

                if (start >= end)
                {
                    start = end - TimeSpan.FromMilliseconds(1);
                }

                if (start < TimeSpan.Zero)
                {
                    start = TimeSpan.Zero;
                }

                results.Add(new TranscriptSegment(utterance.Speaker, start, end, text));
            }

            return MergeSegments(results);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Elimina anotaciones de sonidos no verbales y puntuación repetida
    /// (artefactos habituales de Whisper) dejando el texto listo para mostrar.
    /// </summary>
    public static string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var cleaned = AnnotationRegex.Replace(text, " ");
        cleaned = MultipleSpacesRegex.Replace(cleaned, " ").Trim();
        return RepeatedPunctuationRegex.Replace(cleaned, static m => m.Value[..1]);
    }

    /// <summary>Indica si el texto tiene contenido transcribible (letras o dígitos).</summary>
    public static bool HasContent(string text)
    {
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Fusiona segmentos contiguos del mismo hablante con un hueco menor de
    /// 1 segundo: evita la fragmentación en burbujas de una o dos palabras.
    /// </summary>
    public static IReadOnlyList<TranscriptSegment> MergeSegments(IEnumerable<TranscriptSegment> segments)
    {
        var merged = new List<TranscriptSegment>();
        foreach (var segment in segments)
        {
            if (merged.Count > 0
                && merged[^1].Speaker == segment.Speaker
                && segment.Start - merged[^1].End <= MergeGap)
            {
                var last = merged[^1];
                merged[^1] = last with
                {
                    Text = $"{last.Text} {segment.Text}",
                    End = segment.End > last.End ? segment.End : last.End,
                };
            }
            else
            {
                merged.Add(segment);
            }
        }

        return merged;
    }

    private WhisperProcessor GetProcessor()
    {
        if (_processor is not null && _processorLanguage == Language)
        {
            return _processor;
        }

        _processor?.Dispose();
        var builder = _factory
            .CreateBuilder()
            .WithThreads(Threads)
            .WithNoContext();
        // Nota: WithSuppressRegex se probó y descartó — en whisper.net 1.9.1
        // corrompe la decodificación (texto ilegible y ~70x más lento). El
        // filtrado de anotaciones se hace en CleanText tras decodificar.

        if (string.Equals(Language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            builder.WithLanguageDetection();
        }
        else
        {
            builder.WithLanguage(Language);
            // El prompt inicial en el idioma destino asienta puntuación y
            // mayúsculas desde los primeros segmentos.
            var prompt = GetInitialPrompt(Language);
            if (prompt is not null)
            {
                builder.WithPrompt(prompt);
            }
        }

        _processor = builder.Build();
        _processorLanguage = Language;
        return _processor;
    }

    /// <summary>Prompt natural corto por idioma (como semilla de estilo, no de contenido).</summary>
    public static string? GetInitialPrompt(string language) => language.ToLowerInvariant() switch
    {
        "es" => "Hola, ¿cómo estás? Bien, gracias. ¿Y tú?",
        "en" => "Hi, how are you? I'm fine, thanks. And you?",
        _ => null,
    };

    private static bool IsHallucination(string text, float noSpeechProbability)
    {
        if (noSpeechProbability > 0.9f && text.Length < 25)
        {
            return true;
        }

        foreach (var phrase in HallucinationPhrases)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
