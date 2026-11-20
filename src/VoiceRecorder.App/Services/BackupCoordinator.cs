using VoiceRecorder.Core;
using VoiceRecorder.Storage.Backup;

namespace VoiceRecorder.App.Services;

/// <summary>
/// Une la configuración del backup externo con el almacén local. El backup es
/// siempre manual: nada se sube ni se baja hasta que el usuario lo pide desde
/// la ventana «Sincronizar».
/// </summary>
public sealed class BackupCoordinator : IDisposable
{
    private readonly SettingsService _settings;
    private readonly ISessionFileStore _store;

    public BackupCoordinator(SettingsService settings, ISessionFileStore store)
    {
        _settings = settings;
        _store = store;
        Service = new BackupService(store.RootDirectory);
        ApplySettings();
    }

    /// <summary>Se dispara cuando se descargan archivos a local.</summary>
    public event Action? SessionsDownloaded;

    public BackupService Service { get; }

    /// <summary>Indica si el backup está activo y bien configurado.</summary>
    public bool IsActive => Service.Target is not null;

    /// <summary>
    /// Recalcula el destino a partir de la configuración guardada. Devuelve un
    /// mensaje de error si está activado pero incompleto.
    /// </summary>
    public string? ApplySettings()
    {
        Service.Target = null;
        if (!_settings.BackupEnabled)
        {
            return null;
        }

        try
        {
            Service.Target = CreateTarget(_settings.BackupAccountName, _settings.GetBackupKey(), _settings.BackupContainer);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Crea el destino Azure con los datos indicados (lanza ArgumentException si faltan).</summary>
    public static IBackupTarget CreateTarget(string? accountName, string? key, string? container) =>
        new AzureBlobBackupTarget(accountName ?? string.Empty, key ?? string.Empty, container);

    /// <summary>Descarga las sesiones indicadas y las registra en el historial local.</summary>
    public async Task<(DownloadResult Result, int NewSessions)> DownloadAsync(
        IReadOnlyCollection<string> folders,
        bool overwriteChanged,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var result = await Service.DownloadSessionsAsync(folders, overwriteChanged, cancellationToken, progress);
        var added = _store.Refresh();
        if (result.Downloaded > 0)
        {
            SessionsDownloaded?.Invoke();
        }

        return (result, added);
    }

    public static string Describe(BackupResult result)
    {
        if (result.Error is not null && result.Uploaded == 0 && result.Unchanged == 0)
        {
            return $"Subida: error — {result.Error}";
        }

        var text = $"Subida: {result.Uploaded} archivo(s) subido(s), {result.Unchanged} ya estaban iguales en la nube";
        return result.Failed > 0 ? $"{text}, {result.Failed} con error ({result.Error})." : $"{text}.";
    }

    public static string Describe(DownloadResult result, int newSessions)
    {
        var text = $"Descarga: {result.Downloaded} archivo(s) bajado(s), {result.Unchanged} ya iguales en local";
        if (newSessions > 0)
        {
            text += $", {newSessions} sesión(es) nueva(s) en el historial";
        }

        if (result.Skipped > 0)
        {
            text += $", {result.Skipped} distinto(s) en local sin sobrescribir";
        }

        return result.Failed > 0 ? $"{text}, {result.Failed} con error ({result.Error})." : $"{text}.";
    }

    public void Dispose() => Service.Dispose();
}
