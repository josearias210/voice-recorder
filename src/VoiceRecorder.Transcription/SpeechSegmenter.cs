using VoiceRecorder.Core;
using VoiceRecorder.Transcription.Vad;

namespace VoiceRecorder.Transcription;

/// <summary>Emisión de voz continua de un canal, lista para transcribir.</summary>
/// <param name="Speaker">Hablante asociado al canal.</param>
/// <param name="Samples">Audio mono 16kHz del enunciado (con margen de relleno).</param>
/// <param name="Start">Inicio del enunciado en tiempo activo de sesión.</param>
/// <param name="End">Fin del enunciado en tiempo activo de sesión.</param>
public sealed record Utterance(Speaker Speaker, float[] Samples, TimeSpan Start, TimeSpan End)
{
    public double DurationSeconds => (double)Samples.Length / AudioConstants.TargetSampleRate;
}

/// <summary>
/// Extrae enunciados de un canal aplicando el VAD por ventanas, con
/// histéresis de entrada/salida, duración mínima de voz y límite máximo
/// de longitud por enunciado.
/// </summary>
public sealed class SpeechSegmenter : IDisposable
{
    private const float EnterThreshold = 0.55f;
    private const float ExitThreshold = 0.4f;
    private static readonly TimeSpan MinSpeech = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan EndSilence = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan MaxUtterance = TimeSpan.FromSeconds(29);
    private static readonly TimeSpan Padding = TimeSpan.FromMilliseconds(120);

    private readonly IVad _vad;
    private readonly Speaker _speaker;
    private readonly List<float> _pending = [];
    private readonly List<float> _carry = new(8192);

    private bool _inSpeech;
    private TimeSpan _speechStart;
    private TimeSpan _lastVoiceTime;
    private TimeSpan _pendingStart;
    private TimeSpan _chunkStartTime;
    private bool _disposed;

    public SpeechSegmenter(IVad vad, Speaker speaker)
    {
        _vad = vad ?? throw new ArgumentNullException(nameof(vad));
        _speaker = speaker;
    }

    /// <summary>Enunciado completo detectado (se dispara en el hilo llamador de Feed).</summary>
    public event Action<Utterance>? UtteranceCompleted;

    /// <summary>Intervalos de tiempo activo con voz detectada (para indicador en UI).</summary>
    public event Action<Speaker>? SpeechDetected;

    /// <summary>Canal opcional de diagnóstico (hilo de captura).</summary>
    public Action<string>? Trace;

    public void Feed(float[] samples, TimeSpan chunkStartActiveTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(samples);

        _chunkStartTime = chunkStartActiveTime;
        _carry.AddRange(samples);
        var windowSize = _vad.WindowSize;
        var offset = 0;

        while (offset + windowSize <= _carry.Count)
        {
            var window = _carry.GetRange(offset, windowSize);
            ProcessWindow(window, offset);
            offset += windowSize;
        }

        if (offset > 0)
        {
            _carry.RemoveRange(0, offset);
        }
    }

    /// <summary>Emite el enunciado en curso si lo hay (fin de sesión o pausa).</summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inSpeech && _pending.Count > 0)
        {
            EmitUtterance();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vad.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ProcessWindow(List<float> window, int offsetInChunk)
    {
        var probability = _vad.DetectVoiceProbability(window.ToArray());
        var windowStart = _chunkStartTime.Add(TimeSpan.FromSeconds((double)offsetInChunk / AudioConstants.TargetSampleRate));

        AppendToPending(window);

        if (probability >= EnterThreshold)
        {
            if (!_inSpeech)
            {
                _inSpeech = true;
                _speechStart = windowStart;
                _pendingStart = windowStart;
                Trace?.Invoke($"[{_speaker}] entra voz @{windowStart:hh\\:mm\\:ss\\.ff} p={probability:F2}");
            }

            _lastVoiceTime = windowStart.Add(TimeSpan.FromSeconds((double)window.Count / AudioConstants.TargetSampleRate));
            SpeechDetected?.Invoke(_speaker);
            if (_lastVoiceTime - _speechStart >= MaxUtterance)
            {
                EmitUtterance(continuation: true);
            }

            return;
        }

        if (!_inSpeech || probability >= ExitThreshold)
        {
            return;
        }

        var windowEnd = windowStart.Add(TimeSpan.FromSeconds((double)window.Count / AudioConstants.TargetSampleRate));
        var silenceSince = windowEnd - _lastVoiceTime;
        if (silenceSince >= EndSilence)
        {
            EmitUtterance();
        }
    }

    private void AppendToPending(List<float> window)
    {
        if (_inSpeech)
        {
            _pending.AddRange(window);
        }
        else if (_pending.Count > 0)
        {
            // Relleno inicial (pre-roll) mientras no haya voz confirmada: máx. Padding.
            var maxPadding = (int)(Padding.TotalSeconds * AudioConstants.TargetSampleRate);
            var space = maxPadding - _pending.Count;
            if (space > 0)
            {
                _pending.AddRange(window.Take(space));
            }
        }
    }

    private void EmitUtterance(bool continuation = false)
    {
        var samples = _pending.ToArray();
        _pending.Clear();

        var speechDuration = TimeSpan.FromSeconds((double)samples.Length / AudioConstants.TargetSampleRate);
        if (speechDuration >= MinSpeech)
        {
            Trace?.Invoke($"[{_speaker}] enunciado {_pendingStart:hh\\:mm\\:ss\\.ff} dur={speechDuration.TotalSeconds:F2}s");
            UtteranceCompleted?.Invoke(new Utterance(_speaker, samples, _pendingStart, _lastVoiceTime));
        }
        else
        {
            Trace?.Invoke($"[{_speaker}] descartado dur={speechDuration.TotalSeconds:F2}s");
        }

        if (continuation)
        {
            // Continúa el mismo enunciado largo: preserva el estado de voz.
            _inSpeech = true;
            _pendingStart = _lastVoiceTime;
            _speechStart = _lastVoiceTime;
        }
        else
        {
            _inSpeech = false;
        }
    }
}
