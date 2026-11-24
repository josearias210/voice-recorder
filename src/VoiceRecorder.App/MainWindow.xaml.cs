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

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        new SettingsWindow(ViewModel, _store, _settings, _backup) { Owner = this }.ShowDialog();

    private void HistoryToggle_Click(object sender, RoutedEventArgs e) => ToggleHistory();

    /// <summary>Shows or hides the history side panel; the main screen stays focused on recording.</summary>
    private void ToggleHistory()
    {
        var open = HistoryPanel.Visibility != Visibility.Visible;
        HistoryPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        HistoryButton.Content = open ? "Ocultar historial" : "Historial";
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

