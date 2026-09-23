namespace VoiceRecorder.Core;

/// <summary>
/// Captura un flujo de audio del sistema y emite bloques normalizados
/// (mono, <see cref="AudioConstants.TargetSampleRate"/>, PCM float).
/// Los eventos se disparan en hilos de fondo; los consumidores deben sincronizar.
/// </summary>
public interface IAudioCaptureService : IDisposable
{
    AudioSource Source { get; }

    bool IsCapturing { get; }

    /// <summary>Bloque de audio normalizado disponible.</summary>
    event EventHandler<AudioChunk>? AudioChunkReady;

    /// <summary>La captura terminó (por error del dispositivo o Stop).</summary>
    event EventHandler? Stopped;

    /// <summary>Error irrecuperable de captura.</summary>
    event EventHandler<Exception>? CaptureFailed;

    void Start();

    void Stop();
}
