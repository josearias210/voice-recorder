using VoiceRecorder.Core;
using VoiceRecorder.Transcription;

namespace VoiceRecorder.Tests;

public class WhisperTranscriberTests
{
    [Theory]
    [InlineData(2, 3)]   // máquinas mínimas: se respeta el suelo de 3
    [InlineData(4, 3)]
    [InlineData(8, 7)]
    [InlineData(12, 11)]
    [InlineData(16, 15)]
    [InlineData(24, 16)] // se respeta el techo de 16
    public void ComputeThreads_Dedica_Casi_Todos_Los_Nucleos_A_Whisper(int processorCount, int expected)
    {
        Assert.Equal(expected, WhisperTranscriber.ComputeThreads(processorCount));
    }

    [Theory]
    [InlineData("es", "Hola, ¿cómo estás? Bien, gracias. ¿Y tú?")]
    [InlineData("en", "Hi, how are you? I'm fine, thanks. And you?")]
    [InlineData("auto", null)]
    public void GetInitialPrompt_Devuelve_Semilla_Por_Idioma(string language, string? expected)
    {
        Assert.Equal(expected, WhisperTranscriber.GetInitialPrompt(language));
    }

    [Fact]
    public void CleanText_Elimina_Anotaciones_Y_Puntuacion_Repetida()
    {
        Assert.Equal("Hola, ¿qué tal?", WhisperTranscriber.CleanText(" [Música] Hola, ¿qué tal? "));
        Assert.Equal("Vale, perfecto.", WhisperTranscriber.CleanText("Vale, perfecto... (risas)"));
        Assert.Equal("Seguimos", WhisperTranscriber.CleanText("[Aplausos]  Seguimos"));
    }

    [Fact]
    public void HasContent_Requiere_Letras_O_Digitos()
    {
        Assert.False(WhisperTranscriber.HasContent(WhisperTranscriber.CleanText("...")));
        Assert.False(WhisperTranscriber.HasContent(WhisperTranscriber.CleanText("[Música]")));
        Assert.True(WhisperTranscriber.HasContent(WhisperTranscriber.CleanText("Hola.")));
    }

    [Fact]
    public void MergeSegments_Fusiona_Contiguos_Del_Mismo_Hablante()
    {
        var s1 = new TranscriptSegment(Speaker.Me, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), "Hola");
        var s2 = new TranscriptSegment(Speaker.Me, TimeSpan.FromSeconds(2.4), TimeSpan.FromSeconds(3), "buenos días");
        var s3 = new TranscriptSegment(Speaker.Them, TimeSpan.FromSeconds(3.2), TimeSpan.FromSeconds(5), "¿Qué tal?");
        var s4 = new TranscriptSegment(Speaker.Me, TimeSpan.FromSeconds(6.5), TimeSpan.FromSeconds(8), "Encantado");

        var merged = WhisperTranscriber.MergeSegments([s1, s2, s3, s4]);

        Assert.Equal(3, merged.Count);
        Assert.Equal("Hola buenos días", merged[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(3), merged[0].End);
        Assert.Equal(Speaker.Them, merged[1].Speaker);
        Assert.Equal("Encantado", merged[2].Text);
    }

    [Fact]
    public void MergeSegments_No_Fusiona_Si_El_Hueco_Supera_Un_Segundo()
    {
        var s1 = new TranscriptSegment(Speaker.Me, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), "Uno");
        var s2 = new TranscriptSegment(Speaker.Me, TimeSpan.FromSeconds(3.5), TimeSpan.FromSeconds(4), "Dos");

        var merged = WhisperTranscriber.MergeSegments([s1, s2]);

        Assert.Equal(2, merged.Count);
    }
}
