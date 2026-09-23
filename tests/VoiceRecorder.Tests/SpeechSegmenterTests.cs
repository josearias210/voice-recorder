using VoiceRecorder.Core;
using VoiceRecorder.Transcription;
using VoiceRecorder.Transcription.Vad;
using Xunit;

namespace VoiceRecorder.Tests;

/// <summary>VAD sintético que reproduce una secuencia de probabilidades por ventana.</summary>
internal sealed class FakeVad(float[] probabilities, int windowSize = 512) : IVad
{
    private int _index;

    public int WindowSize => windowSize;

    public float DetectVoiceProbability(ReadOnlySpan<float> window)
    {
        var probability = probabilities[Math.Min(_index, probabilities.Length - 1)];
        _index++;
        return probability;
    }

    public void Reset() => _index = 0;

    public void Dispose()
    {
    }
}

public class SpeechSegmenterTests
{
    private static float[] Audio(int windows) => new float[windows * 512];

    [Fact]
    public void Feed_Con_Voz_Sostenida_Y_Silencio_Emite_Utterance()
    {
        // 8 ventanas con voz (~256ms) + 25 ventanas de silencio (supera el umbral de fin)
        var probabilities = Enumerable.Repeat(0.9f, 8).Concat(Enumerable.Repeat(0.05f, 25)).ToArray();
        using var segmenter = new SpeechSegmenter(new FakeVad(probabilities), Speaker.Them);

        Utterance? emitted = null;
        segmenter.UtteranceCompleted += u => emitted = u;

        segmenter.Feed(Audio(33), TimeSpan.Zero);

        Assert.NotNull(emitted);
        Assert.Equal(Speaker.Them, emitted!.Speaker);
        // La voz son 8 ventanas; con el relleno de silencio posterior debe haber más.
        Assert.InRange(emitted.Samples.Length, 8 * 512, 33 * 512);
        Assert.InRange(emitted.DurationSeconds, 0.2, 11);
    }

    [Fact]
    public void Feed_Voz_Muy_Breve_Se_Descarta_Al_Flush()
    {
        // 2 ventanas de voz (~64ms) + poco silencio y Flush: duración < MinSpeech
        var probabilities = Enumerable.Repeat(0.9f, 2).Concat(Enumerable.Repeat(0.05f, 3)).ToArray();
        using var segmenter = new SpeechSegmenter(new FakeVad(probabilities), Speaker.Me);

        Utterance? emitted = null;
        segmenter.UtteranceCompleted += u => emitted = u;

        segmenter.Feed(Audio(5), TimeSpan.Zero);
        segmenter.Flush();

        Assert.Null(emitted);
    }

    [Fact]
    public void Feed_Solo_Silencio_No_Emite_Nada()
    {
        using var segmenter = new SpeechSegmenter(new FakeVad(Enumerable.Repeat(0.01f, 100).ToArray()), Speaker.Me);

        Utterance? emitted = null;
        segmenter.UtteranceCompleted += u => emitted = u;

        segmenter.Feed(Audio(100), TimeSpan.Zero);
        segmenter.Flush();

        Assert.Null(emitted);
    }

    [Fact]
    public void Timestamps_De_Utterance_Respeta_El_Relog_Del_Feed()
    {
        var probabilities = Enumerable.Repeat(0.9f, 6).Concat(Enumerable.Repeat(0.05f, 25)).ToArray();
        using var segmenter = new SpeechSegmenter(new FakeVad(probabilities), Speaker.Them);

        Utterance? emitted = null;
        segmenter.UtteranceCompleted += u => emitted = u;

        // La sesión ya llevaba 10 segundos activos cuando empieza la voz
        segmenter.Feed(Audio(31), TimeSpan.FromSeconds(10));

        Assert.NotNull(emitted);
        Assert.InRange(emitted!.Start.TotalSeconds, 9.9, 10.15);
        Assert.InRange(emitted.End.TotalSeconds, 10.0, 12);
        Assert.True(emitted.End > emitted.Start);
    }
}
