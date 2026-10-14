using System.IO;
using System.Text.Json;

namespace VoiceRecorder.App.Services;

/// <summary>DTO de serialización (record con constructor posicional, compatible con STJ).</summary>
internal sealed record SettingsData(string? ModelFileName, string? LanguageCode, string? MicrophoneId, bool RecordAudioFirst = false);

/// <summary>
/// Preferencias de usuario persistidas en %LOCALAPPDATA%\VoiceRecorder\settings.json
/// (modelo Whisper, idioma, micrófono y modo de grabación).
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

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
                new SettingsData(ModelFileName, LanguageCode, MicrophoneId, RecordAudioFirst),
                JsonOptions));
        }
        catch
        {
            // Persistencia fallida: no es un error fatal.
        }
    }
}
