using System.Globalization;
using System.Text;
using System.Windows;
using VoiceRecorder.App.Infrastructure;
using VoiceRecorder.App.Services;
using VoiceRecorder.Storage.Backup;

namespace VoiceRecorder.App;

/// <summary>Fila de la comparación local/nube de una sesión.</summary>
public sealed class SyncSessionRow : ViewModelBase
{
    private bool _isSelected;

    public SyncSessionRow(SessionSyncStatus session) => Session = session;

    public SessionSyncStatus Session { get; }

    public SyncState State => Session.State;

    public string Title => Session.Title;

    public string DetailText =>
        $"{(Session.StartedAt is { } at ? at.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) : "sin fecha")} · "
        + $"{Session.Files.Count} archivo(s) · local {SyncFormat.Size(Session.LocalBytes)} · nube {SyncFormat.Size(Session.RemoteBytes)}";

    public string StatusText => Session.State switch
    {
        SyncState.Synced => "✓ Sincronizada",
        SyncState.LocalOnly => "⬆ Solo en local",
        SyncState.RemoteOnly => "⬇ Solo en la nube",
        SyncState.Different => "≠ Distinta",
        _ => string.Join(" · ", new[]
        {
            Session.SyncedCount > 0 ? $"✓ {Session.SyncedCount}" : null,
            Session.LocalOnlyCount > 0 ? $"⬆ {Session.LocalOnlyCount}" : null,
            Session.RemoteOnlyCount > 0 ? $"⬇ {Session.RemoteOnlyCount}" : null,
            Session.DifferentCount > 0 ? $"≠ {Session.DifferentCount}" : null,
        }.Where(s => s is not null)),
    };

    /// <summary>
    /// What has been done with the session: transcribed, summarized, analyzed and uploaded to Notion
    /// (read from its session.json, local or in the cloud).
    /// </summary>
    public string ProcessText => Session.Info is not { } info
        ? "Sin datos de la sesión"
        : string.Join("   ", new[]
        {
            info.SegmentCount > 0 ? $"✓ Transcrita ({info.SegmentCount})" : "○ Sin transcribir",
            info.HasSummary ? "✓ Resumen" : "○ Sin resumen",
            info.HasReview ? "✓ Analizada" : null,
            info.InNotion ? "✓ En Notion" : "○ No subida a Notion",
        }.Where(s => s is not null));

    /// <summary>Detalle por archivo (tooltip).</summary>
    public string FilesText
    {
        get
        {
            var text = new StringBuilder(Session.Folder);
            foreach (var file in Session.Files)
            {
                var name = file.RelativePath[(file.RelativePath.LastIndexOf('/') + 1)..];
                text.AppendLine().Append(SyncFormat.Label(file.State)).Append("  ").Append(name);
            }

            return text.ToString();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

/// <summary>Opción del filtro de la lista.</summary>
public sealed record SyncFilter(string Name, Func<SessionSyncStatus, bool> Matches);

internal static class SyncFormat
{
    public static string Size(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
        >= 1024L * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };

    public static string Label(SyncState state) => state switch
    {
        SyncState.Synced => "✓ sincronizado",
        SyncState.LocalOnly => "⬆ solo en local",
        SyncState.RemoteOnly => "⬇ solo en la nube",
        SyncState.Different => "≠ distinto",
        _ => "mixto",
    };
}

/// <summary>
/// Compara la carpeta local de sesiones con el backup externo, muestra las
/// diferencias (qué falta subir, qué falta bajar, qué es distinto y qué está
/// sincronizado) y permite subir o bajar a mano las sesiones marcadas.
/// </summary>
public partial class SyncWindow : Window
{
    private static readonly SyncFilter[] Filters =
    [
        new("Todas", _ => true),
        new("Pendientes (no sincronizadas)", s => s.State != SyncState.Synced),
        new("⬆ Falta subir", s => s.HasPendingUpload),
        new("⬇ Falta bajar", s => s.HasPendingDownload),
        new("≠ Con diferencias", s => s.DifferentCount > 0),
        new("✓ Sincronizadas", s => s.State == SyncState.Synced),
        new("○ Sin transcribir", s => s.Info is { SegmentCount: 0 }),
        new("○ Sin resumen", s => s.Info is { HasSummary: false }),
        new("✓ Analizadas", s => s.Info is { HasReview: true }),
        new("○ Sin analizar", s => s.Info is { HasReview: false, SegmentCount: > 0 }),
        new("○ No subidas a Notion", s => s.Info is { InNotion: false }),
    ];

    private readonly BackupCoordinator _backup;
    private CancellationTokenSource? _cts;
    private List<SyncSessionRow> _rows = [];

    public SyncWindow(BackupCoordinator backup)
    {
        InitializeComponent();
        _backup = backup;
        TargetText.Text = $"{backup.Service.Target?.DisplayName ?? "Backup externo no configurado"} · "
                          + "solo se sube o baja lo que tú marques; nunca se borra nada en la nube.";
        FilterBox.ItemsSource = Filters;
        FilterBox.SelectedIndex = 0;
        Loaded += async (_, _) => await CompareAsync();
        Closed += (_, _) => _cts?.Cancel();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await CompareAsync();

    private async Task CompareAsync()
    {
        await RunAsync("Comparando archivos locales con la nube…", async token =>
        {
            var comparison = await _backup.Service.CompareAsync(token, new Progress<double>(p => Progress.Value = p));
            ShowComparison(comparison);
        });
    }

    private void ShowComparison(SyncComparison comparison)
    {
        _rows = comparison.Sessions.Select(s => new SyncSessionRow(s)).ToList();
        SelectAllBox.IsChecked = false;

        PercentBar.Value = comparison.SyncedPercent;
        PercentText.Text = comparison.TotalFiles == 0
            ? "No hay archivos ni en local ni en la nube."
            : comparison.IsFullySynced
                ? $"✓ Sincronizado al 100 % — {comparison.TotalFiles} archivo(s) en {comparison.Sessions.Count} sesión(es), iguales en local y en la nube."
                : $"Sincronizado al {Math.Floor(comparison.SyncedPercent):0} % — {comparison.SyncedFiles} de {comparison.TotalFiles} archivo(s) iguales en ambos lados.";

        SyncedValue.Text = Card(comparison.SyncedFiles, comparison.CountSessions(SyncState.Synced));
        LocalOnlyValue.Text = Card(comparison.LocalOnlyFiles, comparison.Sessions.Count(s => s.LocalOnlyCount > 0));
        RemoteOnlyValue.Text = Card(comparison.RemoteOnlyFiles, comparison.Sessions.Count(s => s.RemoteOnlyCount > 0));
        DifferentValue.Text = Card(comparison.DifferentFiles, comparison.Sessions.Count(s => s.DifferentCount > 0));
        ApplyFilter();

        StatusText.Text = comparison.IsFullySynced
            ? "Todo está sincronizado: no hay nada que subir ni bajar."
            : "Marca las sesiones y pulsa «⬆ Subir marcadas» o «⬇ Bajar marcadas». Pasa el ratón por una sesión para ver el estado de cada archivo.";
    }

    private static string Card(int files, int sessions) => $"{files} archivo(s) · {sessions} sesión(es)";

    private void ApplyFilter()
    {
        if (FilterBox.SelectedItem is not SyncFilter filter)
        {
            return;
        }

        var visible = _rows.Where(r => filter.Matches(r.Session)).ToList();
        SessionsList.ItemsSource = visible;
        EmptyText.Text = _rows.Count == 0 ? "No hay sesiones ni en local ni en la nube." : "Ninguna sesión en este filtro.";
        EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Filter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ApplyFilter();

    private IEnumerable<SyncSessionRow> VisibleRows() =>
        SessionsList.ItemsSource as IEnumerable<SyncSessionRow> ?? [];

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var value = SelectAllBox.IsChecked == true;
        foreach (var row in VisibleRows())
        {
            row.IsSelected = value;
        }
    }

    private void SelectPendingUpload_Click(object sender, RoutedEventArgs e) =>
        _rows.ForEach(r => r.IsSelected = r.Session.HasPendingUpload);

    private void SelectPendingDownload_Click(object sender, RoutedEventArgs e) =>
        _rows.ForEach(r => r.IsSelected = r.Session.HasPendingDownload);

    private List<string>? SelectedFolders()
    {
        var folders = _rows.Where(r => r.IsSelected).Select(r => r.Session.Folder).ToList();
        if (folders.Count == 0)
        {
            StatusText.Text = "Marca al menos una sesión.";
            return null;
        }

        return folders;
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFolders() is not { } folders)
        {
            return;
        }

        string? summary = null;
        await RunAsync($"Subiendo {folders.Count} sesión(es)…", async token =>
        {
            var result = await _backup.Service.UploadAsync(token, new Progress<double>(p => Progress.Value = p), folders);
            summary = BackupCoordinator.Describe(result);
        });
        await RefreshAfterAsync(summary);
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFolders() is not { } folders)
        {
            return;
        }

        var overwrite = OverwriteBox.IsChecked == true;
        string? summary = null;
        await RunAsync($"Bajando {folders.Count} sesión(es)…", async token =>
        {
            var (result, added) = await _backup.DownloadAsync(folders, overwrite, new Progress<double>(p => Progress.Value = p), token);
            summary = BackupCoordinator.Describe(result, added);
        });
        await RefreshAfterAsync(summary);
    }

    /// <summary>Vuelve a comparar tras subir/bajar y deja visible el resultado de la operación.</summary>
    private async Task RefreshAfterAsync(string? summary)
    {
        if (summary is null)
        {
            return;
        }

        await CompareAsync();
        StatusText.Text = summary;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task RunAsync(string status, Func<CancellationToken, Task> action)
    {
        if (_cts is not null)
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        _cts = cts;
        RefreshButton.IsEnabled = UploadButton.IsEnabled = DownloadButton.IsEnabled = false;
        StatusText.Text = status;
        Progress.Value = 0;
        Progress.Visibility = Visibility.Visible;
        try
        {
            await action(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Ventana cerrada.
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _cts = null;
            Progress.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = UploadButton.IsEnabled = DownloadButton.IsEnabled = true;
        }
    }
}
