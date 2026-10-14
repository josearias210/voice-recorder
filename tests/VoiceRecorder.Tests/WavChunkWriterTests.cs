using NAudio.Wave;
using VoiceRecorder.Audio;

namespace VoiceRecorder.Tests;

public class WavChunkWriterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"vr-wav-{Guid.NewGuid():N}.wav");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static float[] Chunk(int seed, int count) => Enumerable.Range(0, count)
        .Select(i => (float)Math.Sin((seed + i) * 0.01))
        .ToArray();

    [Fact]
    public void Escribe_Wav_Valido_Con_Los_Bloques_En_Orden()
    {
        using (var writer = new WavChunkWriter(_path))
        {
            writer.WriteSamples(Chunk(0, 16000));
            writer.WriteSamples(Chunk(100, 8000));
            Assert.Equal(24000, writer.SampleCount);
            Assert.Equal(TimeSpan.FromSeconds(1.5), writer.Duration);
        }

        using var reader = new WaveFileReader(_path);
        Assert.Equal(16000, reader.WaveFormat.SampleRate);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(TimeSpan.FromSeconds(1.5), reader.TotalTime);

        var pcm = new byte[reader.Length];
        reader.ReadExactly(pcm);
        var samples = new float[pcm.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
        }

        // Los datos PCM cuantizados coinciden con los floats originales (±1 LSB).
        var expected = Chunk(0, 16000).Concat(Chunk(100, 8000)).ToArray();
        Assert.Equal(expected.Length, samples.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            var quantized = expected[i] * 32767f / 32768f;
            Assert.InRange(samples[i], quantized - 2f / 32768f, quantized + 2f / 32768f);
        }
    }

    [Fact]
    public void Bloques_Vacios_No_Rompen_El_Archivo()
    {
        using (var writer = new WavChunkWriter(_path))
        {
            writer.WriteSamples([]);
            writer.WriteSamples(new float[10]);
            writer.WriteSamples([]);
            Assert.Equal(10, writer.SampleCount);
        }

        using var reader = new WaveFileReader(_path);
        Assert.Equal(TimeSpan.FromSeconds(10d / 16000), reader.TotalTime);
    }
}
