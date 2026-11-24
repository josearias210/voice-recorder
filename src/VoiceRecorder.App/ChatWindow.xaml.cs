using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using VoiceRecorder.App.Services;
using VoiceRecorder.App.ViewModels;
using VoiceRecorder.Core;

namespace VoiceRecorder.App;

public partial class ChatWindow : Window, INotifyPropertyChanged
{
    private readonly ISessionStore _store;
    private readonly Action _onChanged;
    private readonly MainViewModel? _actions;
    private readonly IReadOnlyList<TranscriptSegment> _segments;
    private readonly int _wordCount;
    private bool _dirty;

    public ChatWindow(ISessionStore store, SessionSummary summary, Action onChanged, MainViewModel? actions = null)
    {
        InitializeComponent();
        _store = store;
        _onChanged = onChanged;
        _actions = actions;

        var loaded = store.GetSession(summary.Id)!.Value;
        Summary = loaded.Session;
        _segments = loaded.Segments;
        _wordCount = _segments.Sum(s => s.Text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length);

        Segments = [.. _segments.Select(s => new SegmentVm(s))];
        DataContext = this;
        Title = $"{Summary.Title} · Voice Recorder";
        Loaded += (_, _) => ChatScroll.ScrollToEnd();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SessionSummary Summary { get; private set; }

    public string SessionTitle
    {
        get => Summary.Title;
        set
        {
            Summary = Summary with { Title = value };
            MarkDirty();
        }
    }

    public string SessionTags
    {
        get => Summary.Tags;
        set
        {
            Summary = Summary with { Tags = value };
            MarkDirty();
        }
    }

    public string SessionNotes
    {
        get => Summary.Notes;
        set
        {
            Summary = Summary with { Notes = value };
            MarkDirty();
        }
    }

    /// <summary>Confirmación no modal del último guardado (visible en la cabecera).</summary>
    public string? SaveStatusText { get; private set; }

    public string SessionDateText => $"{Summary.StartedAt:dd/MM/yyyy HH:mm} · {Summary.ActiveDuration:hh\\:mm\\:ss}";

    public string SegmentChip => Segments.Count == 1 ? "1 segmento" : $"{Segments.Count} segmentos";

    public string WordChip => _wordCount == 1 ? "1 palabra" : $"{_wordCount:N0} palabras";

    public List<SegmentVm> Segments { get; }

    public bool HasCallSummary => Summary.CallSummary is not null;

    public string CallSummaryText => Summary.CallSummary?.Text ?? string.Empty;

    public string CallSummaryInfo => Summary.CallSummary is { } s
        ? $"{s.Model} · {s.CreatedAt:dd/MM/yyyy HH:mm} · {CostFormat.Cost(s.CostUsd)}"
        : string.Empty;

    public string SummaryButtonText => HasCallSummary ? "Regenerar resumen" : "Generar resumen";

    public bool IsReviewEnabled => _actions?.IsReviewEnabled == true;

    /// <summary>The review panel is shown while the experiment is on and there is a review.</summary>
    public bool ShowReview => IsReviewEnabled && Summary.Review is not null;

    public string ReviewText => Summary.Review?.Text ?? string.Empty;

    public string ReviewInfo => Summary.Review is { } r
        ? $"{r.Model} · {r.CreatedAt:dd/MM/yyyy HH:mm} · {CostFormat.Cost(r.CostUsd)}"
        : string.Empty;

    public string ReviewButtonText => Summary.Review is null ? "Analizar conversación" : "Reanalizar conversación";

    public bool HasNotionLink => Summary.Notion is not null;

    public string NotionLinkText => Summary.Notion is { } n ? $"Ver en Notion ({n.ExportedAt:dd/MM HH:mm})" : string.Empty;

    /// <summary>Result of the last summary or Notion action.</summary>
    public string? ActionStatus { get; private set; }

    private void MarkDirty([CallerMemberName] string? propertyName = null)
    {
        _dirty = true;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void Save_Click(object sender, RoutedEventArgs e) => SaveChanges();

    /// <summary>Persiste título, etiquetas y notas; devuelve true si el guardado tuvo éxito.</summary>
    private bool SaveChanges()
    {
        try
        {
            _store.UpdateSession(Summary.Id, Summary.Title, Summary.Tags, Summary.Notes);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"No se pudo guardar: {ex.Message}",
                "Voice Recorder",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }

        _dirty = false;
        _onChanged();
        Title = $"{Summary.Title} · Voice Recorder";
        SaveStatusText = $"Guardado ✓ {DateTime.Now:HH:mm:ss}";
        OnPropertyChanged(nameof(SaveStatusText));
        return true;
    }

    private void ExportTxt_Click(object sender, RoutedEventArgs e) => Export(ExportFormat.Txt);

    private void ExportMd_Click(object sender, RoutedEventArgs e) => Export(ExportFormat.Markdown);

    private void ExportSrt_Click(object sender, RoutedEventArgs e) => Export(ExportFormat.Srt);

    private void Export(ExportFormat format)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = ExportService.BuildFileName(Summary, format),
            Filter = format switch
            {
                ExportFormat.Txt => "Texto (*.txt)|*.txt",
                ExportFormat.Markdown => "Markdown (*.md)|*.md",
                ExportFormat.Srt => "Subtítulos (*.srt)|*.srt",
                _ => "Todos (*.*)|*.*",
            },
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllText(
            dialog.FileName,
            ExportService.BuildContent(Summary, _segments, format),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        System.Windows.MessageBox.Show(this, $"Exportado a:\n{dialog.FileName}", "Voice Recorder", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            $"¿Eliminar la sesión \"{Summary.Title}\"?",
            "Confirmar eliminación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _store.DeleteSession(Summary.Id);
        _dirty = false; // La sesión ya no existe: no pedir guardar al cerrar.
        _onChanged();
        Close();
    }

    private async void Summarize_Click(object sender, RoutedEventArgs e)
    {
        if (_actions is null)
        {
            return;
        }

        if (!_actions.CanSummarize)
        {
            SetActionStatus("Guarda tu clave de OpenAI en Configuración › «Transcripción…» para generar resúmenes.");
            return;
        }

        if (HasCallSummary && System.Windows.MessageBox.Show(
                this,
                "Se generará un resumen nuevo con OpenAI y reemplazará el actual. ¿Continuar?",
                "Regenerar resumen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunActionAsync("Generando el resumen con OpenAI…", () => _actions.SummarizeSessionAsync(Summary.Id));
    }

    private async void Review_Click(object sender, RoutedEventArgs e)
    {
        if (_actions is null)
        {
            return;
        }

        if (!_actions.CanSummarize)
        {
            SetActionStatus("Guarda tu clave de OpenAI en Configuración › «Transcripción…» para analizar la conversación.");
            return;
        }

        if (Summary.Review is not null && System.Windows.MessageBox.Show(
                this,
                "Se generará un análisis nuevo con OpenAI y reemplazará el actual. ¿Continuar?",
                "Reanalizar conversación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunActionAsync("Analizando la conversación con OpenAI…", () => _actions.ReviewSessionAsync(Summary.Id));
    }

    private async void UploadNotion_Click(object sender, RoutedEventArgs e)
    {
        if (_actions is null)
        {
            return;
        }

        if (!_actions.IsNotionReady)
        {
            SetActionStatus("Configura Notion en Configuración › «Notion…» (token y base de datos).");
            return;
        }

        await RunActionAsync("Subiendo a Notion…", () => _actions.ExportSessionToNotionAsync(Summary.Id));
    }

    /// <summary>
    /// Saves pending edits (the summary and Notion use the saved title, tags and notes),
    /// runs the action and refreshes the summary and Notion link from the store.
    /// </summary>
    private async Task RunActionAsync(string progressText, Func<Task<bool>> action)
    {
        if (_dirty && !SaveChanges())
        {
            return;
        }

        SummaryButton.IsEnabled = ReviewButton.IsEnabled = NotionButton.IsEnabled = false;
        SetActionStatus(progressText);
        try
        {
            await action();
            SetActionStatus(_actions?.StatusText);
            if (_store.GetSession(Summary.Id) is ({ } fresh, _))
            {
                Summary = Summary with { CallSummary = fresh.CallSummary, Notion = fresh.Notion, Review = fresh.Review };
                foreach (var name in new[]
                {
                    nameof(HasCallSummary), nameof(CallSummaryText), nameof(CallSummaryInfo), nameof(SummaryButtonText),
                    nameof(ShowReview), nameof(ReviewText), nameof(ReviewInfo), nameof(ReviewButtonText),
                    nameof(HasNotionLink), nameof(NotionLinkText),
                })
                {
                    OnPropertyChanged(name);
                }
            }
        }
        finally
        {
            SummaryButton.IsEnabled = ReviewButton.IsEnabled = NotionButton.IsEnabled = true;
        }
    }

    private void SetActionStatus(string? text)
    {
        ActionStatus = text;
        OnPropertyChanged(nameof(ActionStatus));
    }

    private void OpenNotion_Click(object sender, RoutedEventArgs e)
    {
        if (Summary.Notion?.Url is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_dirty)
        {
            var result = System.Windows.MessageBox.Show(
                this,
                "Hay cambios sin guardar (título, etiquetas o notas). ¿Guardarlos antes de cerrar?",
                "Voice Recorder",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == MessageBoxResult.Yes && !SaveChanges())
            {
                e.Cancel = true; // El guardado falló: no se pierden los cambios.
                return;
            }
        }

        _onChanged();
        base.OnClosing(e);
    }
}

