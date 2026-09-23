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
    ];

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
        var cores = Environment.ProcessorCount;
        Threads = Math.Clamp(cores / 2, 2, 8);
    }

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
                var text = segment.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text) || IsHallucination(text, segment.NoSpeechProbability))
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

            return results;
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

        if (string.Equals(Language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            builder.WithLanguageDetection();
        }
        else
        {
            builder.WithLanguage(Language);
        }

        _processor = builder.Build();
        _processorLanguage = Language;
        return _processor;
    }

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
