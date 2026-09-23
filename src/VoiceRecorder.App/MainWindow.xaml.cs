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
    private bool _exitRequested;

    public MainWindow(MainViewModel viewModel, ISessionStore store)
    {
        InitializeComponent();
        _store = store;
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.Segments.CollectionChanged += (_, _) =>
        {
            ChatScroll.ScrollToEnd();
        };
        viewModel.SessionChanged += UpdateTaskbarState;
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

    private void OpenSelectedSession()
    {
        if (HistoryList.SelectedItem is not SessionRowVm row)
        {
            return;
        }

        var chat = new ChatWindow(_store, row.Summary, () => ViewModel.LoadHistory(ViewModel.SearchText))
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

    private void Model_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ViewModel?.RefreshModelAvailability();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            Hide();
            if (ViewModel.HasUnsavedLiveSession)
            {
                App.ShowBalloon("Sigue grabando", "La transcripción continúa en segundo plano. Usa el icono de la bandeja para volver.");
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

