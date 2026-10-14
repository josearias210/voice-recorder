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
    private readonly IReadOnlyList<TranscriptSegment> _segments;
    private readonly int _wordCount;
    private bool _dirty;

    public ChatWindow(ISessionStore store, SessionSummary summary, Action onChanged)
    {
        InitializeComponent();
        _store = store;
        _onChanged = onChanged;

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

