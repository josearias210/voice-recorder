using System.Windows;
using VoiceRecorder.App.Services;
using VoiceRecorder.Storage.Backup;

namespace VoiceRecorder.App;

/// <summary>Configuración del backup externo (Azure Blob Storage).</summary>
public partial class BackupSettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly BackupCoordinator _backup;
    private bool _busy;

    public BackupSettingsWindow(SettingsService settings, BackupCoordinator backup, string localFolder)
    {
        InitializeComponent();
        _settings = settings;
        _backup = backup;

        LocalFolderRun.Text = localFolder;
        EnabledBox.IsChecked = settings.BackupEnabled;
        AccountBox.Text = settings.BackupAccountName ?? string.Empty;
        ContainerBox.Text = string.IsNullOrWhiteSpace(settings.BackupContainer)
            ? AzureBlobBackupTarget.DefaultContainer
            : settings.BackupContainer;
        KeyHint.Text = settings.HasBackupKey
            ? "Hay una clave guardada (cifrada). Déjalo vacío para conservarla o escribe una nueva para reemplazarla."
            : "La clave se guarda cifrada con DPAPI: solo tu usuario de Windows en este equipo puede leerla.";
        if (backup.Service.LastResult is { } last && backup.Service.LastSyncAt is { } at)
        {
            StatusText.Text = $"Último respaldo {at:dd/MM HH:mm}. {BackupCoordinator.Describe(last)}";
        }
    }

    /// <summary>Clave a usar: la escrita ahora o, si está vacía, la guardada.</summary>
    private string? CurrentKey() => KeyBox.Password.Length > 0 ? KeyBox.Password : _settings.GetBackupKey();

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            var target = BackupCoordinator.CreateTarget(AccountBox.Text, CurrentKey(), ContainerBox.Text);
            StatusText.Text = $"Conectando con {target.DisplayName}…";
            await target.EnsureReadyAsync(CancellationToken.None);
            var files = await target.ListAsync(CancellationToken.None);
            StatusText.Text = $"Conexión correcta con {target.DisplayName}. Archivos en el backup: {files.Count}.";
        });
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            EnabledBox.IsChecked = true;
            if (!SaveSettings())
            {
                return;
            }

            StatusText.Text = "Respaldando…";
            Progress.Value = 0;
            Progress.Visibility = Visibility.Visible;
            var result = await _backup.Service.SyncAsync(
                CancellationToken.None,
                new Progress<double>(p => Progress.Value = p));
            StatusText.Text = BackupCoordinator.Describe(result);
        });
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SaveSettings())
        {
            if (_backup.IsActive)
            {
                _backup.SyncInBackground();
            }

            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Valida y guarda la configuración. Devuelve false si falta algún dato.</summary>
    private bool SaveSettings()
    {
        var enabled = EnabledBox.IsChecked == true;
        if (enabled)
        {
            try
            {
                BackupCoordinator.CreateTarget(AccountBox.Text, CurrentKey(), ContainerBox.Text);
            }
            catch (ArgumentException ex)
            {
                StatusText.Text = ex.Message;
                return false;
            }
        }

        _settings.BackupEnabled = enabled;
        _settings.BackupAccountName = AccountBox.Text.Trim();
        _settings.BackupContainer = ContainerBox.Text.Trim();
        if (KeyBox.Password.Length > 0)
        {
            _settings.SetBackupKey(KeyBox.Password);
            KeyBox.Clear();
        }

        _settings.Save();
        var error = _backup.ApplySettings();
        if (error is not null)
        {
            StatusText.Text = error;
            return false;
        }

        return true;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        TestButton.IsEnabled = SyncButton.IsEnabled = SaveButton.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            TestButton.IsEnabled = SyncButton.IsEnabled = SaveButton.IsEnabled = true;
            _busy = false;
        }
    }
}
