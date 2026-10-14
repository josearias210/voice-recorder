using VoiceRecorder.App.Services;

namespace VoiceRecorder.Tests;

public class ThemeServiceTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"vr-theme-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    [Fact]
    public void Sin_Preferencia_El_Tema_Por_Defecto_Es_Oscuro()
    {
        var service = new ThemeService(_dataDir);
        service.Initialize();

        Assert.Equal(AppTheme.Dark, service.Current);
    }

    [Fact]
    public void SetTheme_Persiste_La_Preferencia_Entre_Instancias()
    {
        new ThemeService(_dataDir).SetTheme(AppTheme.Light);

        var restarted = new ThemeService(_dataDir);
        restarted.Initialize();

        Assert.Equal(AppTheme.Light, restarted.Current);
    }

    [Fact]
    public void Preferencia_Corrupta_Cae_Al_Tema_Por_Defecto()
    {
        var service = new ThemeService(_dataDir);
        service.SetTheme(AppTheme.Light);
        File.WriteAllText(Path.Combine(_dataDir, "theme.txt"), "neon-purple");

        var restarted = new ThemeService(_dataDir);
        restarted.Initialize();

        Assert.Equal(AppTheme.Dark, restarted.Current);
    }
}
