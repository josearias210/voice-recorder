using VoiceRecorder.App.Services;

namespace VoiceRecorder.Tests;

public class LegalNoticeTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"vr-legal-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("2000-01-01", true)]
    [InlineData(LegalNotice.Version, false)]
    public void Se_Pide_Aceptacion_Si_No_Se_Acepto_La_Version_Actual(string? accepted, bool expected)
    {
        Assert.Equal(expected, LegalNotice.RequiresAcceptance(accepted));
    }

    [Fact]
    public void El_Resumen_Deja_Clara_La_Responsabilidad_Del_Usuario()
    {
        Assert.Contains("TAL CUAL", LegalNotice.Summary);
        Assert.Contains("ÚNICO RESPONSABLE", LegalNotice.Summary);
        Assert.Contains("consentimiento", LegalNotice.Summary);
    }

    [Fact]
    public void La_Aceptacion_Se_Persiste_Con_Las_Preferencias()
    {
        var settings = new SettingsService(_dataDir) { AcceptedLegalNoticeVersion = LegalNotice.Version };
        settings.Save();

        var reloaded = new SettingsService(_dataDir);
        reloaded.Load();

        Assert.False(LegalNotice.RequiresAcceptance(reloaded.AcceptedLegalNoticeVersion));
    }

    [Fact]
    public void Los_Documentos_Legales_Se_Incluyen_En_La_Salida()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "DISCLAIMER.md")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        var disclaimer = File.ReadAllText(Path.Combine(root!, "DISCLAIMER.md"));
        Assert.Contains("NO se hacen responsables", disclaimer);
        Assert.Contains(LegalNotice.Version, disclaimer);
    }
}
