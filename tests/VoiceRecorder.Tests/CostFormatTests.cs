using VoiceRecorder.App.Services;
using VoiceRecorder.Core;

namespace VoiceRecorder.Tests;

public class CostFormatTests
{
    [Theory]
    [InlineData(0, "gratis")]
    [InlineData(0.0018, "≈ 0,0018 USD")]
    [InlineData(0.27, "≈ 0,270 USD")]
    [InlineData(12.5, "≈ 12,50 USD")]
    public void Cost_Muestra_Gratis_O_El_Importe_Estimado(double usd, string expected)
    {
        Assert.Equal(expected, CostFormat.Cost(usd));
    }

    [Theory]
    [InlineData(45, "45 s")]
    [InlineData(200, "3 min 20 s")]
    [InlineData(3900, "1 h 05 min")]
    public void Duration_Usa_La_Unidad_Mas_Legible(int seconds, string expected)
    {
        Assert.Equal(expected, CostFormat.Duration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Summary_Resume_Motor_Tiempo_Y_Coste()
    {
        var whisper = new TranscriptionRun(DateTime.Now, "Whisper", "Base", TimeSpan.FromSeconds(200), 600, 0);

        Assert.Equal("Whisper · 3 min 20 s · gratis", CostFormat.Summary(whisper));
    }
}
