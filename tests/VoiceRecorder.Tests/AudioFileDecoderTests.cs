using NAudio.Wave;
using VoiceRecorder.Audio;
using VoiceRecorder.Core;

namespace VoiceRecorder.Tests;

public class AudioFileDecoderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"vr-decoder-{Guid.NewGuid():N}.wav");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    /// <summary>Escribe un WAV float con las muestras indicadas.</summary>
    private void WriteWav(float[] samples, WaveFormat format)
    {
        using var writer = new WaveFileWriter(_path, format);
        writer.WriteSamples(samples, 0, samples.Length);
    }

    [Fact]
    public void Open_Wav_16k_Mono_Conserva_Duracion_Y_Muestras()
    {
        var sampleRate = 16000;
        var samples = new float[sampleRate * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 0.5f;
        }

        WriteWav(samples, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1));

        using var source = AudioFileDecoder.Open(_path);
        Assert.Equal(16000, source.SampleRate);
        Assert.InRange(source.Duration.TotalSeconds, 1.9, 2.1);

        var buffer = new float[sampleRate];
        var total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            foreach (var value in buffer.AsSpan(0, read))
            {
                Assert.InRange(value, -1f, 1f);
            }
        }

        Assert.InRange(total, (int)(sampleRate * 1.95), sampleRate * 2);
    }

    [Fact]
    public void Open_Estereo_44k_Resamplea_A_Mono_16k()
    {
        var sampleRate = 44100;
        var frames = sampleRate; // 1 segundo estéreo
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var value = (float)Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 0.5f;
            samples[i * 2] = value;
            samples[(i * 2) + 1] = value;
        }

        WriteWav(samples, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2));

        using var source = AudioFileDecoder.Open(_path);
        Assert.Equal(16000, source.SampleRate);

        var buffer = new float[16000];
        var total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
        }

        // El remuestreo no es exacto: toleramos un pequeño desfase.
        Assert.InRange(total, 15600, 16400);
    }

    [Fact]
    public void Open_Fichero_Inexistente_Lanza_Error_Descriptivo()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => AudioFileDecoder.Open(_path));
        Assert.Contains(Path.GetFileName(_path), exception.Message);
    }
}
