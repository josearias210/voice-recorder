using System.Windows;
using VoiceRecorder.App.Services;
using VoiceRecorder.App.ViewModels;
using VoiceRecorder.Core;

namespace VoiceRecorder.App;

/// <summary>
/// Settings that are not part of the daily flow: device, model, language, engines, costs,
/// backup, theme and app information. Bound to the main view model so changes apply at once.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ISessionStore _store;
    private readonly SettingsService? _settings;
    private readonly BackupCoordinator? _backup;

    public SettingsWindow(MainViewModel viewModel, ISessionStore store, SettingsService? settings, BackupCoordinator? backup)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _store = store;
        _settings = settings;
        _backup = backup;
        DataContext = viewModel;
    }

    private void Model_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        _viewModel.RefreshModelAvailability();

    private void TranscriptionSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is not null)
        {
            new TranscriptionSettingsWindow(_settings) { Owner = this }.ShowDialog();
            _viewModel.RefreshConfigSummary(); // The default engine may have changed.
        }
    }

    private void Costs_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CostLog is { } log)
        {
            new CostsWindow(log) { Owner = this }.ShowDialog();
        }
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null || _backup is null || _store is not ISessionFileStore fileStore)
        {
            return;
        }

        new BackupSettingsWindow(_settings, _backup, fileStore.RootDirectory) { Owner = this }.ShowDialog();
    }

    private void SyncBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_backup is null)
        {
            return;
        }

        if (!_backup.IsActive)
        {
            _viewModel.StatusText = "Configura primero el backup externo (cuenta, contenedor y clave).";
            Backup_Click(sender, e);
        }

        if (_backup.IsActive)
        {
            new SyncWindow(_backup) { Owner = this }.ShowDialog();
        }
    }

    private void LegalNotice_Click(object sender, RoutedEventArgs e) => LegalNotice.OpenDocument("DISCLAIMER.md");

    private void Privacy_Click(object sender, RoutedEventArgs e) => LegalNotice.OpenDocument("PRIVACY.md");

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
