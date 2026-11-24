using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoiceRecorder.Transcription;

namespace VoiceRecorder.App.Services;

/// <summary>DTO de serialización (record con constructor posicional, compatible con STJ).</summary>
internal sealed record SettingsData(
    string? ModelFileName,
    string? LanguageCode,
    string? MicrophoneId,
    string? AcceptedLegalNoticeVersion = null,
    bool BackupEnabled = false,
    string? BackupAccountName = null,
    string? BackupContainer = null,
    string? BackupKeyProtected = null,
    TranscriptionEngineKind DefaultEngine = TranscriptionEngineKind.Whisper,
    string? OpenAiModel = null,
    string? OpenAiKeyProtected = null,
    double? OpenAiUsdPerMinute = null);

/// <summary>
/// Preferencias de usuario persistidas en %LOCALAPPDATA%\VoiceRecorder\settings.json
/// (modelo Whisper, idioma, micrófono, motor de transcripción y respaldo externo).
/// Las claves (backup y OpenAI) se guardan cifradas con DPAPI: solo las
/// descifra el mismo usuario de Windows en este equipo.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly byte[] BackupKeyEntropy = Encoding.UTF8.GetBytes("VoiceRecorder.Backup.v1");
    private static readonly byte[] OpenAiKeyEntropy = Encoding.UTF8.GetBytes("VoiceRecorder.OpenAi.v1");

    private readonly string _filePath;
    private string? _backupKeyProtected;
    private string? _openAiKeyProtected;

    public SettingsService(string? dataDir = null)
    {
        var dir = dataDir
            ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceRecorder");
        Directory.CreateDirectory(dir);
        _filePath = Path.Join(dir, "settings.json");
    }

    public string? ModelFileName { get; set; }

    public string? LanguageCode { get; set; }

    public string? MicrophoneId { get; set; }

    /// <summary>Versión del aviso legal (<see cref="LegalNotice.Version"/>) que el usuario aceptó.</summary>
    public string? AcceptedLegalNoticeVersion { get; set; }

    /// <summary>Backup externo en Azure Blob Storage configurado (desactivado por defecto). La subida siempre es manual.</summary>
    public bool BackupEnabled { get; set; }

    /// <summary>Nombre de la cuenta de Azure Storage.</summary>
    public string? BackupAccountName { get; set; }

    /// <summary>Contenedor de blobs (por defecto "voice-recorder").</summary>
    public string? BackupContainer { get; set; }

    /// <summary>Motor que se propone al transcribir (Whisper local por defecto).</summary>
    public TranscriptionEngineKind DefaultEngine { get; set; } = TranscriptionEngineKind.Whisper;

    /// <summary>Modelo de OpenAI (por defecto <see cref="OpenAiTranscriber.DefaultModel"/>).</summary>
    public string? OpenAiModel { get; set; }

    /// <summary>Precio de OpenAI para estimar costes (USD por minuto); null = precio de referencia.</summary>
    public double? OpenAiUsdPerMinute { get; set; }

    /// <summary>Indica si hay una clave de respaldo guardada.</summary>
    public bool HasBackupKey => !string.IsNullOrEmpty(_backupKeyProtected);

    /// <summary>Indica si hay una clave de OpenAI guardada.</summary>
    public bool HasOpenAiKey => !string.IsNullOrEmpty(_openAiKeyProtected);

    /// <summary>Guarda (cifrada) la clave de la cuenta; null o vacío la borra.</summary>
    public void SetBackupKey(string? key) => _backupKeyProtected = Protect(key, BackupKeyEntropy);

    /// <summary>Clave descifrada, o null si no hay o no se puede descifrar (otro usuario/equipo).</summary>
    public string? GetBackupKey() => Unprotect(_backupKeyProtected, BackupKeyEntropy);

    /// <summary>Guarda (cifrada) la clave de la API de OpenAI; null o vacío la borra.</summary>
    public void SetOpenAiKey(string? key) => _openAiKeyProtected = Protect(key?.Trim(), OpenAiKeyEntropy);

    /// <summary>Clave de OpenAI descifrada, o null si no hay o no se puede descifrar.</summary>
    public string? GetOpenAiKey() => Unprotect(_openAiKeyProtected, OpenAiKeyEntropy);

    /// <summary>Carga las preferencias; si el archivo falta o está corrupto, quedan los valores por defecto.</summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var data = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(_filePath), JsonOptions);
            if (data is null)
            {
                return;
            }

            ModelFileName = data.ModelFileName;
            LanguageCode = data.LanguageCode;
            MicrophoneId = data.MicrophoneId;
            AcceptedLegalNoticeVersion = data.AcceptedLegalNoticeVersion;
            BackupEnabled = data.BackupEnabled;
            BackupAccountName = data.BackupAccountName;
            BackupContainer = data.BackupContainer;
            _backupKeyProtected = data.BackupKeyProtected;
            DefaultEngine = Enum.IsDefined(data.DefaultEngine) ? data.DefaultEngine : TranscriptionEngineKind.Whisper;
            OpenAiModel = data.OpenAiModel;
            _openAiKeyProtected = data.OpenAiKeyProtected;
            OpenAiUsdPerMinute = data.OpenAiUsdPerMinute is > 0 ? data.OpenAiUsdPerMinute : null;
        }
        catch
        {
            // Preferencias ilegibles: se siguen los valores por defecto.
        }
    }

    /// <summary>Guarda las preferencias; sin permisos de escritura, los cambios solo viven en esta sesión.</summary>
    public void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(
                new SettingsData(
                    ModelFileName,
                    LanguageCode,
                    MicrophoneId,
                    AcceptedLegalNoticeVersion,
                    BackupEnabled,
                    BackupAccountName,
                    BackupContainer,
                    _backupKeyProtected,
                    DefaultEngine,
                    OpenAiModel,
                    _openAiKeyProtected,
                    OpenAiUsdPerMinute),
                JsonOptions));
        }
        catch
        {
            // Persistencia fallida: no es un error fatal.
        }
    }

    private static string? Protect(string? secret, byte[] entropy) =>
        string.IsNullOrEmpty(secret)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), entropy, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string? protectedValue, byte[] entropy)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
