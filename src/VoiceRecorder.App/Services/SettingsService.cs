using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VoiceRecorder.App.Services;

/// <summary>DTO de serialización (record con constructor posicional, compatible con STJ).</summary>
internal sealed record SettingsData(
    string? ModelFileName,
    string? LanguageCode,
    string? MicrophoneId,
    bool RecordAudioFirst = false,
    string? AcceptedLegalNoticeVersion = null,
    bool BackupEnabled = false,
    string? BackupAccountName = null,
    string? BackupContainer = null,
    string? BackupKeyProtected = null);

/// <summary>
/// Preferencias de usuario persistidas en %LOCALAPPDATA%\VoiceRecorder\settings.json
/// (modelo Whisper, idioma, micrófono, modo de grabación y respaldo externo).
/// La clave del respaldo se guarda cifrada con DPAPI (solo la descifra el
/// mismo usuario de Windows en este equipo).
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly byte[] KeyEntropy = Encoding.UTF8.GetBytes("VoiceRecorder.Backup.v1");

    private readonly string _filePath;
    private string? _backupKeyProtected;

    public SettingsService(string? dataDir = null)
    {
        var dir = dataDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceRecorder");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
    }

    public string? ModelFileName { get; set; }

    public string? LanguageCode { get; set; }

    public string? MicrophoneId { get; set; }

    /// <summary>
    /// true = grabar el audio a archivo y transcribir al detener (sin
    /// transcripción en vivo); false = transcripción en vivo (por defecto).
    /// </summary>
    public bool RecordAudioFirst { get; set; }

    /// <summary>Versión del aviso legal (<see cref="LegalNotice.Version"/>) que el usuario aceptó.</summary>
    public string? AcceptedLegalNoticeVersion { get; set; }

    /// <summary>Backup externo en Azure Blob Storage configurado (desactivado por defecto). La subida siempre es manual.</summary>
    public bool BackupEnabled { get; set; }

    /// <summary>Nombre de la cuenta de Azure Storage.</summary>
    public string? BackupAccountName { get; set; }

    /// <summary>Contenedor de blobs (por defecto "voice-recorder").</summary>
    public string? BackupContainer { get; set; }

    /// <summary>Indica si hay una clave de respaldo guardada.</summary>
    public bool HasBackupKey => !string.IsNullOrEmpty(_backupKeyProtected);

    /// <summary>Guarda (cifrada) la clave de la cuenta; null o vacío la borra.</summary>
    public void SetBackupKey(string? key)
    {
        _backupKeyProtected = string.IsNullOrEmpty(key)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), KeyEntropy, DataProtectionScope.CurrentUser));
    }

    /// <summary>Clave descifrada, o null si no hay o no se puede descifrar (otro usuario/equipo).</summary>
    public string? GetBackupKey()
    {
        if (string.IsNullOrEmpty(_backupKeyProtected))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(_backupKeyProtected), KeyEntropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

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
            RecordAudioFirst = data.RecordAudioFirst;
            AcceptedLegalNoticeVersion = data.AcceptedLegalNoticeVersion;
            BackupEnabled = data.BackupEnabled;
            BackupAccountName = data.BackupAccountName;
            BackupContainer = data.BackupContainer;
            _backupKeyProtected = data.BackupKeyProtected;
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
                    RecordAudioFirst,
                    AcceptedLegalNoticeVersion,
                    BackupEnabled,
                    BackupAccountName,
                    BackupContainer,
                    _backupKeyProtected),
                JsonOptions));
        }
        catch
        {
            // Persistencia fallida: no es un error fatal.
        }
    }
}
