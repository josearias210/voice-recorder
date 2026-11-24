using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

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
            ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceRecorder");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Join(dir, "theme.txt");
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

        ApplyTitleBars(theme);
    }

    // Windows draws the title bar, so the resource dictionary cannot reach it: ask DWM
    // for the dark variant on every open window and on each window opened later.
    private const int DwmUseImmersiveDarkMode = 20;
    private static AppTheme _titleBarTheme = AppTheme.Dark;
    private static bool _titleBarHookRegistered;

    private static void ApplyTitleBars(AppTheme theme)
    {
        _titleBarTheme = theme;
        if (!_titleBarHookRegistered)
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) => SetTitleBar((Window)sender, _titleBarTheme)));
            _titleBarHookRegistered = true;
        }

        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            SetTitleBar(window, theme);
        }
    }

    private static void SetTitleBar(Window window, AppTheme theme)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return; // Not shown yet: the Loaded handler will apply it.
        }

        var useDark = theme == AppTheme.Dark ? 1 : 0;
        // Fails silently on Windows versions without dark title bars.
        _ = NativeMethods.DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref useDark, sizeof(int));

        // Windows 11: paint the caption and the window border with the theme colours so the
        // title bar blends with the window. Older versions ignore these attributes.
        SetColorAttribute(handle, DwmCaptionColor, "WindowBackground");
        SetColorAttribute(handle, DwmTextColor, "TextBrush");
        SetColorAttribute(handle, DwmBorderColor, "BorderBrush");
    }

    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    private static void SetColorAttribute(IntPtr handle, int attribute, string resourceKey)
    {
        if (System.Windows.Application.Current.TryFindResource(resourceKey) is not System.Windows.Media.SolidColorBrush brush)
        {
            return;
        }

        var color = brush.Color;
        var colorRef = color.R | (color.G << 8) | (color.B << 16); // COLORREF: 0x00BBGGRR
        _ = NativeMethods.DwmSetWindowAttribute(handle, attribute, ref colorRef, sizeof(int));
    }

    private static class NativeMethods
    {
        [DllImport("dwmapi.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
