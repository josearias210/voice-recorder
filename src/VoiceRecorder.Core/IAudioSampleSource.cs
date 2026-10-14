namespace VoiceRecorder.Core;

/// <summary>
/// Fuente de audio mono de lectura secuencial (pull), p. ej. un archivo
/// de audio o vídeo ya normalizado a la frecuencia de transcripción.
/// </summary>
public interface IAudioSampleSource : IDisposable
{
    /// <summary>Frecuencia de muestreo en Hz.</summary>
    int SampleRate { get; }

    /// <summary>Duración total, o <see cref="TimeSpan.Zero"/> si se desconoce.</summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Lee hasta <paramref name="count"/> muestras float en <paramref name="buffer"/>.
    /// Devuelve el número de muestras leídas; 0 indica fin de la fuente.
    /// </summary>
    int Read(float[] buffer, int offset, int count);
}
