namespace VoiceRecorder.Core;

/// <summary>
/// Bloque de audio ya normalizado: mono, <see cref="AudioConstants.TargetSampleRate"/>,
/// muestras PCM float en el rango [-1, 1].
/// </summary>
/// <param name="Source">Canal de origen del audio.</param>
/// <param name="Samples">Muestras mono.</param>
/// <param name="Timestamp">Offset de tiempo activo de la sesión desde el inicio de la captura.</param>
public sealed record AudioChunk(AudioSource Source, float[] Samples, TimeSpan Timestamp)
{
    /// <summary>Duración del bloque en segundos.</summary>
    public double DurationSeconds => (double)Samples.Length / AudioConstants.TargetSampleRate;
}
