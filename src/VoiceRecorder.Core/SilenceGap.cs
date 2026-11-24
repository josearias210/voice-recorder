namespace VoiceRecorder.Core;

/// <summary>
/// Calcula el silencio que falta en una captura que deja de entregar audio
/// cuando no suena nada (WASAPI loopback). Rellenar esos huecos mantiene el
/// canal alineado con el reloj real: el VAD ve el silencio y cierra la frase,
/// y el WAV dura lo mismo que la sesión.
/// </summary>
public static class SilenceGap
{
    /// <summary>Hueco mínimo sin audio a partir del cual se rellena.</summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(300);

    /// <summary>Margen que se deja sin rellenar para el audio real aún en tránsito.</summary>
    public static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Muestras de silencio a insertar para que <paramref name="emittedSamples"/>
    /// alcance el tiempo transcurrido (menos <see cref="Latency"/>), o 0 si el
    /// hueco no llega a <see cref="Threshold"/>.
    /// </summary>
    public static int SamplesToFill(TimeSpan elapsed, long emittedSamples)
    {
        var expected = (long)(elapsed.TotalSeconds * AudioConstants.TargetSampleRate);
        var deficit = expected - emittedSamples;
        var threshold = (long)(Threshold.TotalSeconds * AudioConstants.TargetSampleRate);
        if (deficit < threshold)
        {
            return 0;
        }

        var latency = (long)(Latency.TotalSeconds * AudioConstants.TargetSampleRate);
        return (int)Math.Min(deficit - latency, int.MaxValue);
    }
}
