using VoiceRecorder.App.Services;

namespace VoiceRecorder.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"vr-settings-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    [Fact]
    public void Save_Y_Load_Roundtrip_Conserva_Las_Preferencias()
    {
        var original = new SettingsService(_dataDir)
        {
            ModelFileName = "ggml-small-q5_1.bin",
            LanguageCode = "en",
            MicrophoneId = "mic-usb-1",
            RecordAudioFirst = true,
        };
        original.Save();

        var restarted = new SettingsService(_dataDir);
        restarted.Load();

        Assert.Equal("ggml-small-q5_1.bin", restarted.ModelFileName);
        Assert.Equal("en", restarted.LanguageCode);
        Assert.Equal("mic-usb-1", restarted.MicrophoneId);
        Assert.True(restarted.RecordAudioFirst);
    }

    [Fact]
    public void Sin_Archivo_Los_Valores_Son_Nulos()
    {
        var service = new SettingsService(_dataDir);
        service.Load();

        Assert.Null(service.ModelFileName);
        Assert.Null(service.LanguageCode);
        Assert.Null(service.MicrophoneId);
        Assert.False(service.RecordAudioFirst);
    }

    [Fact]
    public void Archivo_Corrupto_No_Lanza_Y_Vuelve_A_Por_Defecto()
    {
        new SettingsService(_dataDir) { LanguageCode = "en" }.Save();
        File.WriteAllText(Path.Combine(_dataDir, "settings.json"), "{ no soy json");

        var restarted = new SettingsService(_dataDir);
        restarted.Load();

        Assert.Null(restarted.LanguageCode);
    }
}
