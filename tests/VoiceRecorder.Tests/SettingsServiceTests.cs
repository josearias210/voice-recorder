using VoiceRecorder.App.Services;
using VoiceRecorder.Transcription;

namespace VoiceRecorder.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dataDir = Path.Join(Path.GetTempPath(), $"vr-settings-{Guid.NewGuid():N}");

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
        };
        original.Save();

        var restarted = new SettingsService(_dataDir);
        restarted.Load();

        Assert.Equal("ggml-small-q5_1.bin", restarted.ModelFileName);
        Assert.Equal("en", restarted.LanguageCode);
        Assert.Equal("mic-usb-1", restarted.MicrophoneId);
    }

    [Fact]
    public void Save_Y_Load_Conserva_El_Historial_Y_La_Ventana()
    {
        new SettingsService(_dataDir)
        {
            HistoryOpen = true,
            MainWindow = new WindowPlacement(120, 80, 1300, 820, Maximized: true),
        }.Save();

        var restarted = new SettingsService(_dataDir);
        restarted.Load();

        Assert.True(restarted.HistoryOpen);
        Assert.Equal(new WindowPlacement(120, 80, 1300, 820, true), restarted.MainWindow);
    }

    [Fact]
    public void Sin_Archivo_Los_Valores_Son_Nulos()
    {
        var service = new SettingsService(_dataDir);
        service.Load();

        Assert.Null(service.ModelFileName);
        Assert.Null(service.LanguageCode);
        Assert.Null(service.MicrophoneId);
        Assert.Equal(TranscriptionEngineKind.Whisper, service.DefaultEngine);
        Assert.False(service.HasOpenAiKey);
    }

    [Fact]
    public void Archivo_Corrupto_No_Lanza_Y_Vuelve_A_Por_Defecto()
    {
        new SettingsService(_dataDir) { LanguageCode = "en" }.Save();
        File.WriteAllText(Path.Join(_dataDir, "settings.json"), "{ no soy json");

        var restarted = new SettingsService(_dataDir);
        restarted.Load();

        Assert.Null(restarted.LanguageCode);
    }

    [Fact]
    public void Backup_Se_Persiste_Y_La_Clave_Se_Guarda_Cifrada()
    {
        var original = new SettingsService(_dataDir)
        {
            BackupEnabled = true,
            BackupAccountName = "micuenta",
            BackupContainer = "voice-recorder",
        };
        original.SetBackupKey("clave-secreta-de-prueba");
        original.Save();

        Assert.DoesNotContain("clave-secreta-de-prueba", File.ReadAllText(Path.Join(_dataDir, "settings.json")));

        var restarted = new SettingsService(_dataDir);
        restarted.Load();
        Assert.True(restarted.BackupEnabled);
        Assert.Equal("micuenta", restarted.BackupAccountName);
        Assert.Equal("voice-recorder", restarted.BackupContainer);
        Assert.True(restarted.HasBackupKey);
        Assert.Equal("clave-secreta-de-prueba", restarted.GetBackupKey());
    }

    [Fact]
    public void Motor_Y_Clave_De_OpenAI_Se_Persisten_Y_La_Clave_Va_Cifrada()
    {
        var original = new SettingsService(_dataDir)
        {
            DefaultEngine = TranscriptionEngineKind.OpenAi,
            OpenAiModel = "gpt-transcribe",
        };
        original.SetOpenAiKey("  sk-clave-de-prueba  ");
        original.Save();

        Assert.DoesNotContain("sk-clave-de-prueba", File.ReadAllText(Path.Join(_dataDir, "settings.json")));

        var restarted = new SettingsService(_dataDir);
        restarted.Load();
        Assert.Equal(TranscriptionEngineKind.OpenAi, restarted.DefaultEngine);
        Assert.Equal("gpt-transcribe", restarted.OpenAiModel);
        Assert.True(restarted.HasOpenAiKey);
        Assert.Equal("sk-clave-de-prueba", restarted.GetOpenAiKey());
        Assert.Null(restarted.GetBackupKey()); // Claves separadas.

        restarted.SetOpenAiKey(null);
        Assert.False(restarted.HasOpenAiKey);
    }

    [Fact]
    public void Backup_Desactivado_Por_Defecto()
    {
        var service = new SettingsService(_dataDir);
        service.Load();

        Assert.False(service.BackupEnabled);
        Assert.False(service.HasBackupKey);
        Assert.Null(service.GetBackupKey());
    }
}
