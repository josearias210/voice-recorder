using System.IO;
using System.Windows;
using VoiceRecorder.App.Services;
using VoiceRecorder.App.ViewModels;
using VoiceRecorder.Audio;
using VoiceRecorder.Storage;
using VoiceRecorder.Transcription;
using WinForms = System.Windows.Forms;

namespace VoiceRecorder.App;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\VoiceRecorder-SingleInstance";
    private const string ActivateEventName = @"Local\VoiceRecorder-SingleInstance-Activate";

    private static WinForms.NotifyIcon? _trayIcon;
    private static WinForms.ToolStripMenuItem? _menuItemStart;
    private static WinForms.ToolStripMenuItem? _menuItemPause;
    private static WinForms.ToolStripMenuItem? _menuItemStop;
    private static MainWindow? _mainWindow;
    private static SQLiteSessionStore? _store;
    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _activateEvent;
    private static System.Threading.RegisteredWaitHandle? _activateRegistration;
    private static bool _shutdownStarted;

    public static new App Current => (App)System.Windows.Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Hooks de Velopack: SIEMPRE primero (instalación, updates, desinstalación).
        Velopack.VelopackApp.Build().Run();

        base.OnStartup(e);

        // Instancia única: si ya hay otra, la despertamos y salimos.
        _instanceMutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }

            System.Windows.MessageBox.Show(
                "Voice Recorder ya está en ejecución.",
                "Voice Recorder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => ShowMainWindow(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceRecorder");
        _store = new SQLiteSessionStore(Path.Combine(dataDir, "sessions.db"));
        var models = new ModelManager();
        var themes = new ThemeService();
        themes.Initialize();
        var settings = new SettingsService();

        var viewModel = new MainViewModel(_store, models, new WasapiDeviceEnumerator(), themes, settings);
        _mainWindow = new MainWindow(viewModel, _store);
        _mainWindow.DataContext = viewModel;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.StateText) or nameof(MainViewModel.PauseButtonText))
            {
                UpdateTrayState(viewModel.StateText);
            }
        };

        // Apagado de Windows / cierre de sesión: limpiar antes de salir.
        SessionEnding += (_, args) =>
        {
            args.Cancel = true;
            BeginCleanShutdown();
        };

        InitTray(viewModel);
        _mainWindow.Show();
        UpdateTrayState(viewModel.StateText);

        // Comprobación de actualizaciones en segundo plano (5s tras arrancar).
        _ = viewModel.CheckForUpdatesDelayedAsync();
    }

    private static void InitTray(MainViewModel viewModel)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        var icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application;

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Mostrar ventana", null, (_, _) => ShowMainWindow());

        menu.Items.Add(new WinForms.ToolStripSeparator());
        _menuItemStart = (WinForms.ToolStripMenuItem)menu.Items.Add("▶ Iniciar", null, (_, _) => DispatcherInvoke(() => viewModel.StartCommand.Execute(null)));
        _menuItemPause = (WinForms.ToolStripMenuItem)menu.Items.Add("⏸ Pausar / Reanudar", null, (_, _) => DispatcherInvoke(() => viewModel.PauseResumeCommand.Execute(null)));
        _menuItemStop = (WinForms.ToolStripMenuItem)menu.Items.Add("■ Detener", null, (_, _) => DispatcherInvoke(() => viewModel.StopCommand.Execute(null)));

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ((App)Current).ExitApplication());

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "Voice Recorder · Transcriptor de llamadas",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private static void ShowMainWindow()
    {
        DispatcherInvoke(() =>
        {
            _mainWindow?.Show();
            _mainWindow?.Activate();
            if (_mainWindow is { WindowState: WindowState.Minimized } window)
            {
                window.WindowState = WindowState.Normal;
            }
        });
    }

    public static void UpdateTrayState(string stateText)
    {
        if (_menuItemStart is null || _menuItemPause is null || _menuItemStop is null)
        {
            return;
        }

        DispatcherInvoke(() =>
        {
            var vm = _mainWindow?.ViewModel;
            _menuItemStart.Enabled = vm?.CanStart == true;
            var recording = vm is { State: Core.SessionState.Recording or Core.SessionState.Paused, IsFinalizing: false };
            _menuItemPause.Enabled = recording;
            _menuItemStop.Enabled = recording;
            if (_trayIcon is not null && vm is not null)
            {
                _trayIcon.Text = $"Voice Recorder · {stateText}";
            }
        });
    }

    public static void ShowBalloon(string title, string message)
    {
        _trayIcon?.ShowBalloonTip(3000, title, message, WinForms.ToolTipIcon.Info);
    }

    private void ExitApplication() => BeginCleanShutdown();

    /// <summary>
    /// Apaga la app de forma ordenada: cancela importaciones, detiene y guarda
    /// la sesión activa y libera los recursos antes de Shutdown.
    /// </summary>
    private static void BeginCleanShutdown()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        DispatcherInvoke(async () =>
        {
            if (_mainWindow?.ViewModel is { } viewModel)
            {
                viewModel.CancelImport();
                await viewModel.StopIfActiveAsync();
                viewModel.DisposeResources();
            }

            Current.Shutdown();
        });
    }

    private static void DispatcherInvoke(Action action)
    {
        Current.Dispatcher.Invoke(action);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var viewModel = _mainWindow?.ViewModel;
        viewModel?.CancelImport();
        viewModel?.DisposeResources();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _activateRegistration?.Unregister(null);
        _activateEvent?.Dispose();
        _store?.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // El hilo actual no es el propietario (arranque duplicado): nada que liberar.
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
