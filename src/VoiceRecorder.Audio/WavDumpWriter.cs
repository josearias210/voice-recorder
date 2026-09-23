using NAudio.Wave;
using VoiceRecorder.Core;

namespace VoiceRecorder.Audio;

/// <summary>Escribe bloques normalizados a un archivo WAV float (utilidad de verificación).</summary>
public static class WavDumpWriter
{
    /// <summary>Escribe la secuencia de chunks mono 16kHz a un WAV IEEE float.</summary>
    public static void Write(string path, IEnumerable<AudioChunk> chunks)
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(AudioConstants.TargetSampleRate, 1);
        using var writer = new WaveFileWriter(path, format);
        foreach (var chunk in chunks)
        {
            writer.WriteSamples(chunk.Samples, 0, chunk.Samples.Length);
        }
    }
}
