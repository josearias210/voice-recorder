using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceRecorder.Core;

namespace VoiceRecorder.Audio;

/// <summary>
/// Abre archivos de audio o vídeo y los normaliza a mono 16kHz float,
/// con la misma cadena de normalización que la captura en vivo.
/// </summary>
public static class AudioFileDecoder
{
    /// <summary>
    /// Abre el archivo indicado como fuente mono 16kHz. Media Foundation
    /// (vía NAudio) cubre mp3, wav, m4a, mp4, mkv, mov, avi, wma, flac… según
    /// los códecs instalados en el sistema.
    /// </summary>
    public static IAudioSampleSource Open(string path)
    {
        AudioFileReader reader;
        try
        {
            reader = new AudioFileReader(path);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"No se pudo decodificar '{Path.GetFileName(path)}'. Comprueba que el formato esté soportado y el códec instalado.",
                ex);
        }

        try
        {
            return new NormalizedSampleSource(reader);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>Fuente con remuestreo a 16kHz y mezcla a mono de un archivo.</summary>
    private sealed class NormalizedSampleSource : IAudioSampleSource
    {
        private readonly AudioFileReader _reader;
        private readonly ISampleProvider _pipeline;

        public NormalizedSampleSource(AudioFileReader reader)
        {
            _reader = reader;
            var resampler = new WdlResamplingSampleProvider(reader, AudioConstants.TargetSampleRate);
            _pipeline = new MonoSampleProvider(resampler);
        }

        public int SampleRate => AudioConstants.TargetSampleRate;

        public TimeSpan Duration => _reader.TotalTime;

        public int Read(float[] buffer, int offset, int count) => _pipeline.Read(buffer, offset, count);

        public void Dispose() => _reader.Dispose();
    }
}
