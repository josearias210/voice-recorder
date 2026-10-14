using VoiceRecorder.Core;
using VoiceRecorder.Transcription;
using VoiceRecorder.Transcription.Vad;
using Xunit;

namespace VoiceRecorder.Tests;

/// <summary>
/// Máquina de estados de sesión: Recording ⇄ Paused → Stopped, con
/// reanudaciones que no deben consumir nuevas instancias de VAD y con el
/// modo sin motor (grabar primero) que no instancia VAD ni transcribe.
/// </summary>
public class ConversationTranscriberTests
{
    private sealed class FakeCapture(AudioSource source) : IAudioCaptureService
    {
        public AudioSource Source { get; } = source;

        public bool IsCapturing { get; private set; }

        public event EventHandler<AudioChunk>? AudioChunkReady;

        public event EventHandler? Stopped;

#pragma warning disable CS0067 // El fallo de captura no se simula en el fake.
        public event EventHandler<Exception>? CaptureFailed;
#pragma warning restore CS0067

        public void Start() => IsCapturing = true;

        public void Stop()
        {
            IsCapturing = false;
            Stopped?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Simula un bloque entrante de la captura real.</summary>
        public void Emit(int samples = 512) =>
            AudioChunkReady?.Invoke(this, new AudioChunk(Source, new float[samples], TimeSpan.Zero));

        public void Dispose()
        {
        }
    }

    private sealed class FakeEngine : ITranscriptionEngine
    {
        public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(Utterance utterance, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TranscriptSegment>>([]);
    }

    private static (ConversationTranscriber Transcriber, FakeCapture Mic, FakeCapture Loop) Create(Action<int>? onVadCreated = null)
    {
        var mic = new FakeCapture(AudioSource.Microphone);
        var loop = new FakeCapture(AudioSource.Loopback);
        var transcriber = new ConversationTranscriber(
            new FakeEngine(),
            () =>
            {
                onVadCreated?.Invoke(1);
                return new FakeVad([0.01f]);
            },
            mic,
            loop);
        return (transcriber, mic, loop);
    }

    [Fact]
    public async Task Start_Pause_Resume_Stop_Actualiza_El_Estado_De_Sesion()
    {
        var (transcriber, _, _) = Create();
        using var _ = transcriber;

        Assert.Equal(SessionState.Idle, transcriber.State);

        transcriber.Start();
        Assert.Equal(SessionState.Recording, transcriber.State);

        transcriber.Pause();
        Assert.Equal(SessionState.Paused, transcriber.State);

        transcriber.Resume();
        Assert.Equal(SessionState.Recording, transcriber.State);

        await transcriber.StopAsync();
        Assert.Equal(SessionState.Stopped, transcriber.State);
    }

    [Fact]
    public async Task Resume_Tras_Pausa_Reutiliza_Los_Vad_Existentes()
    {
        var vadsCreated = 0;
        var (transcriber, _, _) = Create(_ => Interlocked.Increment(ref vadsCreated));
        using var _ = transcriber;

        transcriber.Start();
        for (var i = 0; i < 3; i++)
        {
            transcriber.Pause();
            Assert.Equal(SessionState.Paused, transcriber.State);
            transcriber.Resume();
            Assert.Equal(SessionState.Recording, transcriber.State);
        }

        // Solo los dos del arranque (micrófono + loopback): reanudar no recarga VAD.
        Assert.Equal(2, vadsCreated);

        await transcriber.StopAsync();
        Assert.Equal(SessionState.Stopped, transcriber.State);
    }

    [Fact]
    public async Task Chunks_Mientras_Grabando_Llegan_Por_AudioTapped()
    {
        var (transcriber, mic, loop) = Create();
        using var _ = transcriber;

        var tapped = new List<AudioChunk>();
        transcriber.AudioTapped += tapped.Add;

        transcriber.Start();
        mic.Emit();
        loop.Emit();
        mic.Emit(256);

        Assert.Equal(3, tapped.Count);
        Assert.Equal(AudioSource.Microphone, tapped[0].Source);
        Assert.Equal(AudioSource.Loopback, tapped[1].Source);
        Assert.Equal(256, tapped[2].Samples.Length);

        // En pausa no se aceptan bloques.
        transcriber.Pause();
        mic.Emit();
        Assert.Equal(3, tapped.Count);
    }

    [Fact]
    public async Task Modo_Sin_Motor_Graba_Sin_Instanciar_Vad_Ni_Transcribir()
    {
        var vadsCreated = 0;
        var mic = new FakeCapture(AudioSource.Microphone);
        var loop = new FakeCapture(AudioSource.Loopback);
        using var transcriber = new ConversationTranscriber(
            engine: null,
            () => { Interlocked.Increment(ref vadsCreated); return new FakeVad([0.01f]); },
            mic,
            loop);

        var tapped = 0;
        transcriber.AudioTapped += _ => Interlocked.Increment(ref tapped);

        transcriber.Start();
        Assert.Equal(SessionState.Recording, transcriber.State);
        Assert.Equal(0, vadsCreated); // Sin motor no hay segmentadores ni VAD.

        mic.Emit();
        loop.Emit();

        transcriber.Pause();
        transcriber.Resume();
        mic.Emit();
        await transcriber.StopAsync();

        Assert.Equal(SessionState.Stopped, transcriber.State);
        Assert.Equal(3, tapped);
        Assert.Equal(0, vadsCreated);
    }
}
