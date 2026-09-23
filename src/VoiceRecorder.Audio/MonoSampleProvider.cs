using NAudio.Wave;

namespace VoiceRecorder.Audio;

/// <summary>Convierte un ISampleProvider multi-canal a mono promediando canales.</summary>
public sealed class MonoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float[] _scratch = new float[8192];

    public MonoSampleProvider(ISampleProvider source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public WaveFormat WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(_source.WaveFormat.SampleRate, 1);

    public int Read(float[] buffer, int offset, int count)
    {
        var channels = _source.WaveFormat.Channels;
        var framesRequested = count;
        var samplesNeeded = framesRequested * channels;

        if (_scratch.Length < samplesNeeded)
        {
            _scratch = new float[samplesNeeded];
        }

        var read = _source.Read(_scratch, 0, samplesNeeded);
        var frames = read / channels;

        for (var i = 0; i < frames; i++)
        {
            float sum = 0f;
            for (var c = 0; c < channels; c++)
            {
                sum += _scratch[(i * channels) + c];
            }

            buffer[offset + i] = sum / channels;
        }

        return frames;
    }
}
