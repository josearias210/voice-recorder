using Velopack;

namespace VoiceRecorder.App.Services;

/// <summary>
/// Comprueba y aplica actualizaciones vía Velopack contra GitHub Releases.
/// La variable de entorno VR_UPDATE_SOURCE permite apuntar a una carpeta local
/// de paquetes vpk para probar el flujo de actualización sin publicar.
/// </summary>
public sealed class UpdateService
{
    private const string DefaultSource = "https://github.com/josearias210/voice-recorder";

    private readonly UpdateManager? _manager;

    public UpdateService()
    {
        var source = Environment.GetEnvironmentVariable("VR_UPDATE_SOURCE");
        var url = string.IsNullOrWhiteSpace(source) ? DefaultSource : source.Trim();
        try
        {
            _manager = new UpdateManager(url);
        }
        catch
        {
            // Fuente inválida: la app funciona sin actualizaciones.
            _manager = null;
        }
    }

    /// <summary>Solo hay soporte de update si la app fue instalada con vpk (Setup).</summary>
    public bool IsSupported => _manager?.IsInstalled == true;

    public string? CurrentVersion => _manager?.CurrentVersion?.ToString();

    /// <summary>Devuelve info de actualización o null si estamos al día.</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        if (!IsSupported)
        {
            return null;
        }

        try
        {
            return await _manager!.CheckForUpdatesAsync();
        }
        catch
        {
            return null;
        }
    }

    public Task DownloadAsync(UpdateInfo info, Action<int> progress, CancellationToken cancellationToken)
    {
        return _manager!.DownloadUpdatesAsync(info, p => progress(p), cancellationToken);
    }

    /// <summary>Aplica la actualización y reinicia la aplicación.</summary>
    public void ApplyAndRestart(UpdateInfo info)
    {
        _manager!.ApplyUpdatesAndRestart(info);
    }
}
