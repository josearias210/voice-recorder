using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using VoiceRecorder.App.ViewModels;
using VoiceRecorder.Core;

namespace VoiceRecorder.App;

public partial class MainWindow : Window
{
    private readonly ISessionStore _store;
    private readonly Services.SettingsService? _settings;
    private readonly Services.BackupCoordinator? _backup;
    private bool _exitRequested;

    public MainWindow(
        MainViewModel viewModel,
        ISessionStore store,
        Services.SettingsService? settings = null,
        Services.BackupCoordinator? backup = null)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;
        _backup = backup;
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.Segments.CollectionChanged += (_, _) =>
        {
            ChatScroll.ScrollToEnd();
        };
        viewModel.SessionImported += OpenImportedSession;
        viewModel.SessionChanged += UpdateTaskbarState;
        viewModel.ChooseEngine = ChooseEngine;
        RestoreWindowState();
    }

    /// <summary>Restores the saved size, position and history panel; ignores positions off every screen.</summary>
    private void RestoreWindowState()
    {
        if (_settings is null)
        {
            return;
        }

        SetHistoryOpen(_settings.HistoryOpen, persist: false);
        if (_settings.MainWindow is not { } placement)
        {
            return;
        }

        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var bounds = new Rect(placement.Left, placement.Top, Math.Max(placement.Width, MinWidth), Math.Max(placement.Height, MinHeight));
        if (!screen.IntersectsWith(bounds))
        {
            return; // The monitor it was on is gone: keep the default centred window.
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowState()
    {
        if (_settings is null)
        {
            return;
        }

        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        _settings.MainWindow = new Services.WindowPlacement(
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized);
        _settings.Save();
    }

    /// <summary>Muestra el diálogo «¿Transcribir? ¿Con qué motor?». Null = no transcribir ahora.</summary>
    private Transcription.TranscriptionEngineKind? ChooseEngine(TranscriptionPrompt prompt)
    {
        if (!IsVisible)
        {
            // Ventana oculta en la bandeja: se muestra para que el usuario vea la pregunta.
            Show();
            Activate();
        }

        var dialog = new TranscribeDialog(prompt, _settings, ViewModel.SelectedModel.DisplayName) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.SelectedEngine : null;
    }

    private SettingsWindow? _settingsWindow;

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>
    /// Opens Settings without blocking this window, so Pause/Stop stay usable while recording.
    /// Only one instance: a second request brings the open one to the front.
    /// </summary>
    internal void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(ViewModel, _store, _settings, _backup) { Owner = this };
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            ViewModel.RefreshConfigSummary(); // The default engine may have changed.
        };
        _settingsWindow.Show();
    }

    private void HistoryToggle_Click(object sender, RoutedEventArgs e) => ToggleHistory();

    /// <summary>Shows or hides the history side panel; the main screen stays focused on recording.</summary>
    private void ToggleHistory() => SetHistoryOpen(HistoryPanel.Visibility != Visibility.Visible);

    /// <summary>Opens the history panel (from the tray menu).</summary>
    internal void ShowHistory() => SetHistoryOpen(true);

    private void SetHistoryOpen(bool open, bool persist = true)
    {
        HistoryPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        HistoryButton.Content = open ? "Ocultar historial" : "Historial";
        if (persist && _settings is not null && _settings.HistoryOpen != open)
        {
            _settings.HistoryOpen = open;
            _settings.Save();
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ToggleHistory();
            e.Handled = true;
        }
    }

    private async void TranscribeSession_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not SessionRowVm row)
        {
            ViewModel.StatusText = "Selecciona en el historial la sesión que quieres transcribir.";
            return;
        }

        await ViewModel.RequestSessionTranscriptionAsync(row.Id);
    }

    public MainViewModel ViewModel { get; }

    private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.SearchCommand.Execute(null);
        }
    }

    private void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedSession();

    private void OpenSession_Click(object sender, RoutedEventArgs e) => OpenSelectedSession();

    private void OpenSessionFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_store is not ISessionFileStore fileStore)
        {
            return;
        }

        var folder = HistoryList.SelectedItem is SessionRowVm row
            ? fileStore.GetSessionFolder(row.Id) ?? fileStore.RootDirectory
            : fileStore.RootDirectory;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void OpenSelectedSession()
    {
        if (HistoryList.SelectedItem is not SessionRowVm row)
        {
            return;
        }

        OpenSessionChat(row.Summary);
    }

    private void OpenImportedSession(long sessionId)
    {
        if (_store.GetSession(sessionId) is not (var summary, _))
        {
            return;
        }

        OpenSessionChat(summary);
    }

    private void OpenSessionChat(SessionSummary summary)
    {
        var chat = new ChatWindow(_store, summary, () => ViewModel.LoadHistory(ViewModel.SearchText))
        {
            Owner = this,
        };
        chat.Show();
    }

    private void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not SessionRowVm row)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            $"¿Eliminar la sesión \"{row.Title}\" y su transcripción?",
            "Confirmar eliminación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _store.DeleteSession(row.Id);
        ViewModel.LoadHistory(ViewModel.SearchText);
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        var mediaFiles = files.Where(MainViewModel.IsMediaFile).ToArray();
        if (mediaFiles.Length > 0)
        {
            _ = ViewModel.TranscribeFilesAsync(mediaFiles);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveWindowState();
        if (!_exitRequested)
        {
            e.Cancel = true;
            Hide();
            if (ViewModel.HasUnsavedLiveSession)
            {
                App.ShowBalloon("Sigue grabando", "La grabación continúa en segundo plano. Usa el icono de la bandeja para volver.");
            }

            return;
        }

        ViewModel.SessionChanged -= UpdateTaskbarState;
        base.OnClosing(e);
    }

    internal void RequestExit()
    {
        _exitRequested = true;
        Dispatcher.InvokeAsync(async () =>
        {
            await ViewModel.StopIfActiveAsync();
            Close();
            System.Windows.Application.Current.Shutdown();
        });
    }

    private void UpdateTaskbarState()
    {
        App.UpdateTrayState(ViewModel.StateText);
    }
}

