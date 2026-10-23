using VoiceRecorder.Core;
using VoiceRecorder.Transcription;
using VoiceRecorder.Transcription.Vad;

namespace VoiceRecorder.Tests;

/// <summary>VAD que marca todas las ventanas como voz (determinista).</summary>
internal sealed class AlwaysVoiceVad : IVad
{
    public int WindowSize => 512;

    public float DetectVoiceProbability(ReadOnlySpan<float> window) => 1f;

    public void Reset()
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>Motor que traduce cada enunciado a un segmento con el mismo rango temporal.</summary>
internal sealed class FakeFileEngine : ITranscriptionEngine
{
    public int Calls;

    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(Utterance utterance, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        IReadOnlyList<TranscriptSegment> segments =
        [
            new TranscriptSegment(utterance.Speaker, utterance.Start, utterance.End, "texto de prueba"),
        ];
        return Task.FromResult(segments);
    }
}

/// <summary>Fuente de muestras en memoria (mono 16kHz).</summary>
internal class FakeSampleSource(float[] samples) : IAudioSampleSource
{
    private int _position;

    public int SampleRate => 16000;

    public virtual TimeSpan Duration => TimeSpan.FromSeconds((double)samples.Length / 16000);

    public int Read(float[] buffer, int offset, int count)
    {
        var read = Math.Min(count, samples.Length - _position);
        Array.Copy(samples, _position, buffer, offset, read);
        _position += read;
        return read;
    }

    public void Dispose()
    {
    }
}

/// <summary>Fuente sin duración conocida (caso real de algunos contenedores).</summary>
internal sealed class UntimedSampleSource(float[] samples) : FakeSampleSource(samples)
{
    public override TimeSpan Duration => TimeSpan.Zero;
}

/// <summary>Recolector de progreso síncrono (determinista en tests).</summary>
internal sealed class CollectingProgress : IProgress<double>
{
    public List<double> Reports { get; } = [];

    public void Report(double value) => Reports.Add(value);
}

/// <summary>Motor que se queda esperando hasta que se cancele el token.</summary>
internal sealed class CancellingEngine : ITranscriptionEngine
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => _entered.Task;

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(Utterance utterance, CancellationToken cancellationToken)
    {
        _entered.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return [];
    }
}

public class FileTranscriberTests
{
    private static float[] Audio(int seconds) => new float[seconds * 16000];

    [Fact]
    public async Task TranscribeAsync_Audio_Con_Voz_Emite_Segmentos_En_Rango()
    {
        using var source = new FakeSampleSource(Audio(10));
        var transcriber = new FileTranscriber(new FakeFileEngine(), () => new AlwaysVoiceVad());

        var progress = new CollectingProgress();
        var segments = await transcriber.TranscribeAsync(source, progress, CancellationToken.None);

        Assert.NotEmpty(segments);
        Assert.All(segments, segment =>
        {
            Assert.Equal(Speaker.Unknown, segment.Speaker);
            Assert.InRange(segment.Start.TotalSeconds, 0, 10);
            Assert.InRange(segment.End.TotalSeconds, 0, 10.01);
        });
        Assert.Equal(1, progress.Reports.Last());
        Assert.True(progress.Reports.SequenceEqual(progress.Reports.OrderBy(p => p)));
    }

    [Fact]
    public async Task TranscribeAsync_Audio_Largo_Se_Divide_Por_El_Maximo_De_Enunciado()
    {
        using var source = new FakeSampleSource(Audio(70));
        var engine = new FakeFileEngine();
        var transcriber = new FileTranscriber(engine, () => new AlwaysVoiceVad());

        var segments = await transcriber.TranscribeAsync(source, null, CancellationToken.None);

        // 70s supera el máximo de 29s por enunciado: al menos 3 trozos.
        Assert.True(engine.Calls >= 3);
        Assert.True(segments.Count >= 3);
    }

    [Fact]
    public async Task TranscribeAsync_Archivo_Largo_No_Hace_Retroceder_El_Progreso()
    {
        using var source = new FakeSampleSource(Audio(70));
        var progress = new CollectingProgress();
        var transcriber = new FileTranscriber(new FakeFileEngine(), () => new AlwaysVoiceVad());

        await transcriber.TranscribeAsync(source, progress, CancellationToken.None);

        Assert.All(progress.Reports, value => Assert.InRange(value, 0, 1));
        Assert.True(progress.Reports.SequenceEqual(progress.Reports.OrderBy(value => value)));
        Assert.Equal(1, progress.Reports.Last());
    }

    [Fact]
    public async Task TranscribeAsync_Archivo_Vacio_Cancelado_No_Se_Completa()
    {
        using var source = new FakeSampleSource([]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var transcriber = new FileTranscriber(new FakeFileEngine(), () => new AlwaysVoiceVad());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transcriber.TranscribeAsync(source, null, cancellation.Token));
    }

    [Fact]
    public async Task TranscribeAsync_Audio_Corto_Emite_Utterance_Al_Flush()
    {
        using var source = new FakeSampleSource(Audio(3));
        var engine = new FakeFileEngine();
        var transcriber = new FileTranscriber(engine, () => new AlwaysVoiceVad());

        var segments = await transcriber.TranscribeAsync(source, null, CancellationToken.None);

        // Sin silencio de cierre, el enunciado se emite en el Flush final.
        Assert.Equal(1, engine.Calls);
        var segment = Assert.Single(segments);
        Assert.InRange(segment.Start.TotalSeconds, 0, 0.1);
        Assert.InRange(segment.End.TotalSeconds, 2.9, 3.01);
    }

    [Fact]
    public async Task TranscribeAsync_Etiqueta_Los_Segmentos_Con_El_Hablante_Indicado()
    {
        // Modo "grabar primero": cada canal (micrófono / llamada) se transcribe
        // con su hablante para conservar la atribución Yo/Ellos.
        using var source = new FakeSampleSource(Audio(3));
        var transcriber = new FileTranscriber(new FakeFileEngine(), () => new AlwaysVoiceVad(), Speaker.Me);

        var segments = await transcriber.TranscribeAsync(source, null, CancellationToken.None);

        Assert.NotEmpty(segments);
        Assert.All(segments, segment => Assert.Equal(Speaker.Me, segment.Speaker));
    }

    [Fact]
    public async Task TranscribeAsync_Propaga_La_Cancelacion()
    {
        using var source = new FakeSampleSource(Audio(60));
        var engine = new CancellingEngine();
        var transcriber = new FileTranscriber(engine, () => new AlwaysVoiceVad());
        using var cts = new CancellationTokenSource();

        var task = transcriber.TranscribeAsync(source, null, cts.Token);
        await engine.Entered;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task TranscribeAsync_Duracion_Desconocida_Transcribe_Sin_Progreso_Intermedio()
    {
        using var source = new UntimedSampleSource(Audio(5));
        var engine = new FakeFileEngine();
        var transcriber = new FileTranscriber(engine, () => new AlwaysVoiceVad());
        var progress = new CollectingProgress();

        var segments = await transcriber.TranscribeAsync(source, progress, CancellationToken.None);

        Assert.Equal(1, engine.Calls);
        var segment = Assert.Single(segments);
        Assert.Equal(Speaker.Unknown, segment.Speaker);
        // Sin duración no hay progreso cuantificable; solo el 1 final.
        var report = Assert.Single(progress.Reports);
        Assert.Equal(1, report);
    }
}
