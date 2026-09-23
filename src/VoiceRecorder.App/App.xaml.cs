using System.IO;
using System.Windows;
using VoiceRecorder.App.ViewModels;
using VoiceRecorder.Audio;
using VoiceRecorder.Storage;
using VoiceRecorder.Transcription;
using WinForms = System.Windows.Forms;

namespace VoiceRecorder.App;

public partial class App : System.Windows.Application
{
    private static WinForms.NotifyIcon? _trayIcon;
    private static WinForms.ToolStripMenuItem? _menuItemStart;
    private static WinForms.ToolStripMenuItem? _menuItemPause;
    private static WinForms.ToolStripMenuItem? _menuItemStop;
    private static MainWindow? _mainWindow;

    public static new App Current => (App)System.Windows.Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Hooks de Velopack: SIEMPRE primero (instalación, updates, desinstalación).
        Velopack.VelopackApp.Build().Run();

        base.OnStartup(e);

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceRecorder");
        var store = new SQLiteSessionStore(Path.Combine(dataDir, "sessions.db"));
        var models = new ModelManager();

        var viewModel = new MainViewModel(store, models, new WasapiDeviceEnumerator());
        _mainWindow = new MainWindow(viewModel, store);
        _mainWindow.DataContext = viewModel;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.StateText) or nameof(MainViewModel.PauseButtonText))
            {
                UpdateTrayState(viewModel.StateText);
            }
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
            var recording = vm?.State is Core.SessionState.Recording or Core.SessionState.Paused;
            _menuItemStart.Enabled = vm is { State: Core.SessionState.Idle or Core.SessionState.Stopped };
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

    private void ExitApplication()
    {
        DispatcherInvoke(async () =>
        {
            if (_mainWindow is not null)
            {
                await _mainWindow.ViewModel.StopIfActiveAsync();
            }

            Shutdown();
        });
    }

    private static void DispatcherInvoke(Action action)
    {
        Current.Dispatcher.Invoke(action);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        base.OnExit(e);
    }
}
