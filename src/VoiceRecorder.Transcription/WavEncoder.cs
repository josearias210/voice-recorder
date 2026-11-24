using System.Buffers.Binary;
using VoiceRecorder.Core;

namespace VoiceRecorder.Transcription;

/// <summary>Codifica muestras mono float como WAV PCM de 16 bits (para enviarlas a una API).</summary>
public static class WavEncoder
{
    private const int HeaderSize = 44;

    public static byte[] Encode(ReadOnlySpan<float> samples, int sampleRate = AudioConstants.TargetSampleRate)
    {
        var dataSize = samples.Length * 2;
        var wav = new byte[HeaderSize + dataSize];
        var span = wav.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], HeaderSize - 8 + dataSize);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16); // tamaño del bloque fmt
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1); // mono
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * 2); // bytes por segundo
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2); // bytes por muestra
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16); // bits por muestra
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataSize);

        var data = span[HeaderSize..];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(data[(i * 2)..], value);
        }

        return wav;
    }
}
