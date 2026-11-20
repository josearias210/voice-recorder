using VoiceRecorder.Core;
using VoiceRecorder.Storage.Backup;

namespace VoiceRecorder.App.Services;

/// <summary>
/// Une la configuración de respaldo externo con el almacén local: cada vez que
/// se guarda o modifica una sesión, programa una copia en Azure Blob Storage
/// con la misma estructura de carpetas.
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
        Service.Completed += result => StatusChanged?.Invoke(Describe(result));
        _store.Changed += Service.RequestSync;
        ApplySettings();
    }

    /// <summary>Texto de estado tras cada sincronización (hilo de fondo).</summary>
    public event Action<string>? StatusChanged;

    public BackupService Service { get; }

    /// <summary>Indica si el respaldo está activo y bien configurado.</summary>
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

    /// <summary>Lanza una sincronización completa en segundo plano (p. ej. al arrancar).</summary>
    public void SyncInBackground() => Service.RequestSync();

    public static string Describe(BackupResult result)
    {
        if (result.Error is not null && result.Uploaded == 0 && result.Unchanged == 0)
        {
            return $"Respaldo externo: error — {result.Error}";
        }

        var text = $"Respaldo externo: {result.Uploaded} archivo(s) subido(s), {result.Unchanged} ya al día";
        return result.Failed > 0 ? $"{text}, {result.Failed} con error ({result.Error})." : $"{text}.";
    }

    public void Dispose()
    {
        _store.Changed -= Service.RequestSync;
        Service.Dispose();
    }
}
