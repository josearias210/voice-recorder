using System.Reflection;

namespace VoiceRecorder.App.Services;

/// <summary>Datos de la aplicación que se muestran al usuario (versión).</summary>
public static class AppInfo
{
    /// <summary>
    /// Versión de la app sin metadatos de compilación (por ejemplo, "1.3.0").
    /// Sale de &lt;Version&gt; en Directory.Build.props o de -p:Version en el release.
    /// </summary>
    public static string Version { get; } = Normalize(
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3));

    /// <summary>Quita el sufijo "+commit" que añade el SDK a la versión informativa.</summary>
    internal static string Normalize(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "0.0.0";
        }

        var plus = informationalVersion.IndexOf('+');
        return (plus >= 0 ? informationalVersion[..plus] : informationalVersion).Trim();
    }
}
