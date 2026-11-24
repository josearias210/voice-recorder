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
        Assert.False(service.AutoSummarize);
        Assert.False(service.NotionEnabled);
        Assert.False(service.IsNotionReady);
        Assert.True(service.NotionAskAfterTranscription);
        Assert.False(service.ReviewEnabled);
        Assert.Equal(
            VoiceRecorder.Storage.Notion.NotionContent.Summary | VoiceRecorder.Storage.Notion.NotionContent.Transcript,
            service.NotionUploadContent);
    }

    [Fact]
    public void El_Analisis_Solo_Se_Sube_Con_El_Experimento_Activo()
    {
        var service = new SettingsService(_dataDir) { NotionIncludeSummary = false, NotionIncludeReview = true };

        Assert.Equal(VoiceRecorder.Storage.Notion.NotionContent.Transcript, service.NotionUploadContent);

        service.ReviewEnabled = true;
        service.Save();
        var restarted = new SettingsService(_dataDir);
        restarted.Load();
        Assert.True(restarted.ReviewEnabled);
        Assert.Equal(
            VoiceRecorder.Storage.Notion.NotionContent.Transcript | VoiceRecorder.Storage.Notion.NotionContent.Review,
            restarted.NotionUploadContent);
    }

    [Fact]
    public void Save_Y_Load_Conserva_Resumen_Y_Notion_Con_El_Token_Cifrado()
    {
        var original = new SettingsService(_dataDir)
        {
            AutoSummarize = true,
            SummaryModel = "otro-modelo",
            SummaryUsdPerMillionInput = 1.5,
            SummaryUsdPerMillionOutput = 6,
            NotionEnabled = true,
            NotionDatabaseId = "01234567-89ab-cdef-0123-456789abcdef",
            NotionAskAfterTranscription = false,
        };
        original.SetNotionToken(" ntn_secreto ");
        original.Save();

        Assert.DoesNotContain("ntn_secreto", File.ReadAllText(Path.Join(_dataDir, "settings.json")));
        var restarted = new SettingsService(_dataDir);
        restarted.Load();
        Assert.True(restarted.AutoSummarize);
        Assert.Equal("otro-modelo", restarted.SummaryModel);
        Assert.Equal(1.5, restarted.SummaryUsdPerMillionInput);
        Assert.Equal(6, restarted.SummaryUsdPerMillionOutput);
        Assert.False(restarted.NotionAskAfterTranscription);
        Assert.Equal("ntn_secreto", restarted.GetNotionToken());
        Assert.True(restarted.IsNotionReady);
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
