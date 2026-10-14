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
    // Sobre voz pura (sin relleno): filtra clics/ruidos breves sin perder
    // palabras cortas ("sí", "no" duran ~150-250ms).
    private static readonly TimeSpan MinSpeech = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan EndSilence = TimeSpan.FromMilliseconds(550);
    private static readonly TimeSpan MaxUtterance = TimeSpan.FromSeconds(29);
    private static readonly TimeSpan Padding = TimeSpan.FromMilliseconds(200);

    private const int InitialCarrySamples = 16000;

    private readonly IVad _vad;
    private readonly Speaker _speaker;
    private readonly object _gate = new();
    private readonly List<float> _pending = [];

    /// <summary>Sobrante de alimentación pendiente de completar una ventana (buffer propio, sin allocs por ventana).</summary>
    private float[] _carry = new float[InitialCarrySamples];
    private int _carryCount;

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

        lock (_gate)
        {
            _chunkStartTime = chunkStartActiveTime;
            if (_carry.Length < _carryCount + samples.Length)
            {
                Array.Resize(ref _carry, Math.Max(_carry.Length * 2, _carryCount + samples.Length));
            }

            Array.Copy(samples, 0, _carry, _carryCount, samples.Length);
            _carryCount += samples.Length;

            var windowSize = _vad.WindowSize;
            var offset = 0;
            while (offset + windowSize <= _carryCount)
            {
                ProcessWindow(_carry.AsSpan(offset, windowSize), offset);
                offset += windowSize;
            }

            if (offset > 0)
            {
                Array.Copy(_carry, offset, _carry, 0, _carryCount - offset);
                _carryCount -= offset;
            }
        }
    }

    /// <summary>Emite el enunciado en curso si lo hay (fin de sesión o pausa).</summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_inSpeech && _pending.Count > 0)
            {
                EmitUtterance();
            }
        }
    }

    /// <summary>
    /// Vuelve al estado inicial (tras una pausa): descarta audio pendiente y
    /// reinicia el estado del VAD sin crear una nueva sesión de inferencia.
    /// </summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            _carryCount = 0;
            _pending.Clear();
            _inSpeech = false;
            _speechStart = TimeSpan.Zero;
            _lastVoiceTime = TimeSpan.Zero;
            _pendingStart = TimeSpan.Zero;
            _chunkStartTime = TimeSpan.Zero;
            _vad.Reset();
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

    private void ProcessWindow(ReadOnlySpan<float> window, int offsetInChunk)
    {
        var probability = _vad.DetectVoiceProbability(window);
        var windowStart = _chunkStartTime.Add(TimeSpan.FromSeconds((double)offsetInChunk / AudioConstants.TargetSampleRate));

        // Se activa el estado de voz ANTES de almacenar la ventana: la ventana
        // con el arranque de la voz nunca se pierde por el relleno.
        if (probability >= EnterThreshold && !_inSpeech)
        {
            _inSpeech = true;
            _speechStart = windowStart;
            _pendingStart = windowStart;
            Trace?.Invoke($"[{_speaker}] entra voz @{windowStart:hh\\:mm\\:ss\\.ff} p={probability:F2}");
        }

        AppendToPending(window);

        if (probability >= EnterThreshold)
        {
            _lastVoiceTime = windowStart.Add(TimeSpan.FromSeconds((double)window.Length / AudioConstants.TargetSampleRate));
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

        var windowEnd = windowStart.Add(TimeSpan.FromSeconds((double)window.Length / AudioConstants.TargetSampleRate));
        var silenceSince = windowEnd - _lastVoiceTime;
        if (silenceSince >= EndSilence)
        {
            EmitUtterance();
        }
    }

    private void AppendToPending(ReadOnlySpan<float> window)
    {
        if (_inSpeech)
        {
            foreach (var sample in window)
            {
                _pending.Add(sample);
            }
        }
        else
        {
            // Relleno inicial (pre-roll) constante mientras no haya voz: máx. Padding.
            // Se acumula desde el principio para no cortar el arranque del enunciado.
            var maxPadding = (int)(Padding.TotalSeconds * AudioConstants.TargetSampleRate);
            var space = maxPadding - _pending.Count;
            if (space > 0)
            {
                foreach (var sample in window[..Math.Min(space, window.Length)])
                {
                    _pending.Add(sample);
                }
            }
        }
    }

    private void EmitUtterance(bool continuation = false)
    {
        var samples = _pending.ToArray();
        _pending.Clear();

        // Duración de voz real (sin contar pre-roll ni silencio final): evita
        // emitir clics/ruidos breves que el relleno haría pasar el umbral.
        var speechDuration = _lastVoiceTime - _speechStart;
        if (speechDuration >= MinSpeech)
        {
            Trace?.Invoke($"[{_speaker}] enunciado {_pendingStart:hh\\:mm\\:ss\\.ff} voz={speechDuration.TotalSeconds:F2}s");
            UtteranceCompleted?.Invoke(new Utterance(_speaker, samples, _pendingStart, _lastVoiceTime));
        }
        else
        {
            Trace?.Invoke($"[{_speaker}] descartado voz={speechDuration.TotalSeconds:F2}s");
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
