namespace VoiceRecorder.Transcription.Vad;

/// <summary>
/// Detector de actividad de voz sobre ventanas de audio mono 16kHz.
/// Las implementaciones mantienen estado interno (contexto, ruido) y no son thread-safe.
/// </summary>
public interface IVad : IDisposable
{
    /// <summary>Número de muestras que espera <see cref="DetectVoiceProbability"/>.</summary>
    int WindowSize { get; }

    /// <summary>Probabilidad de voz [0..1] para la ventana dada.</summary>
    float DetectVoiceProbability(ReadOnlySpan<float> window);

    /// <summary>Reinicia el estado interno (nueva sesión).</summary>
    void Reset();
}
