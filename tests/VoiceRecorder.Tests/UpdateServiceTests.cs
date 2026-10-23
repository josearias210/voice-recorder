using Velopack.Sources;
using VoiceRecorder.App.Services;

namespace VoiceRecorder.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("https://github.com/josearias210/voice-recorder")]
    public void Fuente_De_GitHub_Usa_Las_Publicaciones(string? source)
    {
        Assert.IsType<GithubSource>(UpdateService.CreateSource(source));
    }

    [Fact]
    public void Fuente_Local_Conserva_El_Flujo_De_Prueba()
    {
        var source = UpdateService.CreateSource(Path.GetTempPath());

        Assert.IsType<SimpleFileSource>(source);
    }

    [Fact]
    public void Fuente_Web_Usa_El_Feed_Estatico()
    {
        var source = UpdateService.CreateSource("https://example.com/releases");

        Assert.IsType<SimpleWebSource>(source);
    }
}
