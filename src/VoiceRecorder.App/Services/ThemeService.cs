using System.IO;
using System.Windows;

namespace VoiceRecorder.App.Services;

/// <summary>Tema visual de la aplicación.</summary>
public enum AppTheme
{
    Dark,
    Light,
}

/// <summary>
/// Selecciona y persiste el tema de la aplicación intercambiando el
/// diccionario de recursos (pinceles) en tiempo de ejecución.
/// </summary>
public sealed class ThemeService
{
    private readonly string _settingsPath;

    public ThemeService(string? dataDir = null)
    {
        var dir = dataDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceRecorder");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "theme.txt");
    }

    /// <summary>Tema activo (por defecto: oscuro).</summary>
    public AppTheme Current { get; private set; } = AppTheme.Dark;

    /// <summary>Carga la preferencia persistida y aplica sus recursos. Llamar al arrancar.</summary>
    public void Initialize()
    {
        try
        {
            if (File.Exists(_settingsPath) && Enum.TryParse(File.ReadAllText(_settingsPath).Trim(), out AppTheme saved))
            {
                Current = saved;
            }
        }
        catch
        {
            // Preferencia ilegible: se usa el tema por defecto.
        }

        ApplyResources(Current);
    }

    /// <summary>Cambia el tema, actualiza los recursos y persiste la preferencia.</summary>
    public void SetTheme(AppTheme theme)
    {
        Current = theme;
        ApplyResources(theme);
        try
        {
            File.WriteAllText(_settingsPath, theme.ToString());
        }
        catch
        {
            // Sin permisos de escritura: el cambio solo vive en esta sesión.
        }
    }

    private static void ApplyResources(AppTheme theme)
    {
        if (System.Windows.Application.Current is null)
        {
            return; // Contexto sin aplicación WPF (tests unitarios).
        }

        var source = theme == AppTheme.Dark ? "Themes/Theme.Dark.xaml" : "Themes/Theme.Light.xaml";
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var dictionary = new ResourceDictionary { Source = new Uri($"pack://application:,,,/{source}") };
        if (dictionaries.Count > 0)
        {
            dictionaries[0] = dictionary;
        }
        else
        {
            dictionaries.Add(dictionary);
        }
    }
}
