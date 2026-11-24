using VoiceRecorder.Core;

namespace VoiceRecorder.Tests;

/// <summary>
/// Relleno de silencio del loopback: sin audio reproduciéndose, WASAPI no
/// entrega muestras y el canal se quedaría atrás del reloj real.
/// </summary>
public class SilenceGapTests
{
    private const int Rate = AudioConstants.TargetSampleRate;

    [Fact]
    public void Hueco_Menor_Que_El_Umbral_No_Se_Rellena()
    {
        // 1 s transcurrido, 0,8 s entregados: 200 ms de hueco (< 300 ms).
        Assert.Equal(0, SilenceGap.SamplesToFill(TimeSpan.FromSeconds(1), (long)(0.8 * Rate)));
    }

    [Fact]
    public void Hueco_Mayor_Que_El_Umbral_Se_Rellena_Dejando_El_Margen_De_Latencia()
    {
        // 2 s transcurridos, 1 s entregado: se rellenan 900 ms (1 s - 100 ms de margen).
        Assert.Equal(Rate - (Rate / 10), SilenceGap.SamplesToFill(TimeSpan.FromSeconds(2), Rate));
    }

    [Fact]
    public void Sin_Audio_Desde_El_Inicio_Se_Rellena_Todo_Menos_El_Margen()
    {
        Assert.Equal((5 * Rate) - (Rate / 10), SilenceGap.SamplesToFill(TimeSpan.FromSeconds(5), 0));
    }

    [Fact]
    public void Canal_Adelantado_Al_Reloj_No_Se_Rellena()
    {
        Assert.Equal(0, SilenceGap.SamplesToFill(TimeSpan.FromSeconds(1), 2 * Rate));
    }
}
