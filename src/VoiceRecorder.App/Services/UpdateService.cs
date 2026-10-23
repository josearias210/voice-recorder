using Velopack;
using Velopack.Sources;

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
        try
        {
            _manager = new UpdateManager(CreateSource(source));
        }
        catch
        {
            // Fuente inválida: la app funciona sin actualizaciones.
            _manager = null;
        }
    }

    internal static IUpdateSource CreateSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return new GithubSource(DefaultSource, null, false);
        }

        var path = source.Trim();
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                ? new GithubSource(path, null, false)
                : new SimpleWebSource(path);
        }

        return new SimpleFileSource(new System.IO.DirectoryInfo(path));
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
