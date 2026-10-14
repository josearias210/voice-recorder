using VoiceRecorder.Core;
using VoiceRecorder.Transcription.Vad;

namespace VoiceRecorder.Transcription;

/// <summary>
/// Transcribe archivos completos (audio o vídeo): alimenta un
/// <see cref="SpeechSegmenter"/> con el audio decodificado y transcribe cada
/// enunciado conforme se detecta, con progreso [0..1].
/// </summary>
public sealed class FileTranscriber
{
    private const int ReadChunkSamples = 16000 * 2;

    private readonly ITranscriptionEngine _engine;
    private readonly Func<IVad> _vadFactory;
    private readonly Speaker _speaker;

    public FileTranscriber(ITranscriptionEngine engine, Func<IVad> vadFactory, Speaker speaker = Speaker.Unknown)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _vadFactory = vadFactory ?? throw new ArgumentNullException(nameof(vadFactory));
        _speaker = speaker;
    }

    /// <summary>Canal opcional de diagnóstico (hilo de procesamiento).</summary>
    public Action<string>? Trace;

    /// <summary>Segmento transcrito (hilo de procesamiento).</summary>
    public event Action<TranscriptSegment>? SegmentReady;

    /// <summary>
    /// Transcribe la fuente completa. Los timestamps de los segmentos son
    /// absolutos respecto al inicio del archivo.
    /// </summary>
    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        IAudioSampleSource source,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var sampleRate = source.SampleRate > 0 ? source.SampleRate : AudioConstants.TargetSampleRate;
        var totalSeconds = source.Duration.TotalSeconds;

        var segmenter = new SpeechSegmenter(_vadFactory(), _speaker) { Trace = Trace };
        var pending = new Queue<Utterance>();
        segmenter.UtteranceCompleted += pending.Enqueue;

        var segments = new List<TranscriptSegment>();
        try
        {
            var buffer = new float[ReadChunkSamples];
            var fed = TimeSpan.Zero;
            var transcribedUntil = TimeSpan.Zero;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                segmenter.Feed(buffer[..read], fed);
                fed += TimeSpan.FromSeconds((double)read / sampleRate);
                if (totalSeconds > 0)
                {
                    progress?.Report(0.5 * fed.TotalSeconds / totalSeconds);
                }

                transcribedUntil = await DrainAsync(pending, segments, totalSeconds, transcribedUntil, progress, cancellationToken);
            }

            segmenter.Flush();
            transcribedUntil = await DrainAsync(pending, segments, totalSeconds, transcribedUntil, progress, cancellationToken);
            progress?.Report(1);
            return segments;
        }
        finally
        {
            segmenter.Dispose();
        }
    }

    /// <summary>Transcribe los enunciados pendientes; devuelve hasta dónde se ha transcrito.</summary>
    private async Task<TimeSpan> DrainAsync(
        Queue<Utterance> pending,
        List<TranscriptSegment> segments,
        double totalSeconds,
        TimeSpan transcribedUntil,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var utterance = pending.Dequeue();
            foreach (var segment in await _engine.TranscribeAsync(utterance, cancellationToken))
            {
                segments.Add(segment);
                SegmentReady?.Invoke(segment);
            }

            if (utterance.End > transcribedUntil)
            {
                transcribedUntil = utterance.End;
            }

            if (totalSeconds > 0)
            {
                progress?.Report(0.5 + 0.5 * transcribedUntil.TotalSeconds / totalSeconds);
            }
        }

        return transcribedUntil;
    }
}
