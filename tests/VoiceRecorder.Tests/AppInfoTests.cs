using VoiceRecorder.App.Services;

namespace VoiceRecorder.Tests;

public class AppInfoTests
{
    [Theory]
    [InlineData("1.3.0", "1.3.0")]
    [InlineData("1.3.0+0a1b2c3d", "1.3.0")]
    [InlineData("2.0.0-beta.1+abc", "2.0.0-beta.1")]
    [InlineData(null, "0.0.0")]
    [InlineData("", "0.0.0")]
    public void Normalize_Quita_Metadatos_De_Compilacion(string? input, string expected)
    {
        Assert.Equal(expected, AppInfo.Normalize(input));
    }

    [Fact]
    public void Version_Coincide_Con_La_Del_Ensamblado()
    {
        var assemblyVersion = typeof(AppInfo).Assembly.GetName().Version!;

        Assert.StartsWith($"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}", AppInfo.Version);
        Assert.DoesNotContain("+", AppInfo.Version);
    }
}
