using VoiceRecorder.Core;
using VoiceRecorder.Transcription;

namespace VoiceRecorder.Tests;

/// <summary>
/// Agrupado de frases en bloques para Whisper: más audio por llamada sin
/// perder los tiempos reales de cada frase.
/// </summary>
public class UtteranceBatcherTests
{
    private const int Rate = AudioConstants.TargetSampleRate;

    private static Utterance Make(double startSeconds, double seconds, Speaker speaker = Speaker.Me)
    {
        var start = TimeSpan.FromSeconds(startSeconds);
        return new Utterance(speaker, new float[(int)(seconds * Rate)], start, start + TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void Frases_Que_Caben_Se_Unen_En_Un_Bloque()
    {
        var batcher = new UtteranceBatcher();

        Assert.Null(batcher.Add(Make(0, 2)));
        Assert.Null(batcher.Add(Make(10, 3)));
        var block = batcher.Flush();

        Assert.NotNull(block);
        Assert.Equal(5 * Rate, block.Samples.Length);
        Assert.Equal(TimeSpan.FromSeconds(0), block.Start);
        Assert.Equal(TimeSpan.FromSeconds(13), block.End);
        Assert.Null(batcher.Flush());
    }

    [Fact]
    public void Los_Tiempos_Del_Bloque_Respetan_El_Hueco_Entre_Frases()
    {
        var block = UtteranceBatcher.Combine([Make(0, 2), Make(10, 3)]);

        // 1 s dentro del bloque cae en la primera frase; 3 s cae en la segunda
        // (que empieza en el segundo 2 del bloque y en el 10 de la sesión).
        Assert.Equal(TimeSpan.FromSeconds(1), block.ToSessionTime(TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(11), block.ToSessionTime(TimeSpan.FromSeconds(3)));
        Assert.Equal(TimeSpan.FromSeconds(13), block.ToSessionTime(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Superar_El_Maximo_Cierra_El_Bloque_Anterior()
    {
        var batcher = new UtteranceBatcher();

        Assert.Null(batcher.Add(Make(0, 20)));
        var completed = batcher.Add(Make(25, 10)); // 20 + 10 > 28 s

        Assert.NotNull(completed);
        Assert.Equal(20 * Rate, completed.Samples.Length);
        Assert.Equal(TimeSpan.FromSeconds(25), batcher.Flush()!.Start);
    }

    [Fact]
    public void Cambio_De_Hablante_Cierra_El_Bloque()
    {
        var batcher = new UtteranceBatcher();

        Assert.Null(batcher.Add(Make(0, 2, Speaker.Me)));
        var completed = batcher.Add(Make(3, 2, Speaker.Them));

        Assert.NotNull(completed);
        Assert.Equal(Speaker.Me, completed.Speaker);
        Assert.Equal(Speaker.Them, batcher.Flush()!.Speaker);
    }

    [Fact]
    public void Una_Sola_Frase_Se_Devuelve_Tal_Cual()
    {
        var utterance = Make(4, 2);

        var block = UtteranceBatcher.Combine([utterance]);

        Assert.Same(utterance, block);
        Assert.Equal(TimeSpan.FromSeconds(5), block.ToSessionTime(TimeSpan.FromSeconds(1)));
    }
}
