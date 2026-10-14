using NAudio.Wave;
using VoiceRecorder.Core;

namespace VoiceRecorder.Audio;

/// <summary>
/// Escribe bloques de audio ya normalizado (mono, <see cref="AudioConstants.TargetSampleRate"/>,
/// float [-1..1]) en un WAV PCM 16 bits, de forma incremental: pensado para
/// volcar la grabación a disco mientras se captura y finalizar la cabecera al
/// cerrar. No es thread-safe: cada instancia debe recibir bloques de un único
/// canal/hilo (el que emite su captura).
/// </summary>
public sealed class WavChunkWriter : IDisposable
{
    public const int BitsPerSample = 16;

    private readonly WaveFileWriter _writer;
    private long _sampleCount;

    public WavChunkWriter(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _writer = new WaveFileWriter(path, new WaveFormat(AudioConstants.TargetSampleRate, BitsPerSample, 1));
        FilePath = path;
    }

    /// <summary>Ruta del WAV en curso.</summary>
    public string FilePath { get; }

    /// <summary>Muestras (frames mono) escritas hasta el momento.</summary>
    public long SampleCount => _sampleCount;

    /// <summary>Duración escrita hasta el momento.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds((double)_sampleCount / AudioConstants.TargetSampleRate);

    public void WriteSamples(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        _writer.WriteSamples(samples.ToArray(), 0, samples.Length);
        _sampleCount += samples.Length;
    }

    public void Dispose() => _writer.Dispose();
}
