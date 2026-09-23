using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using VoiceRecorder.App.Services;
using VoiceRecorder.App.ViewModels;
using VoiceRecorder.Core;

namespace VoiceRecorder.App;

public partial class ChatWindow : Window
{
    private readonly ISessionStore _store;
    private readonly Action _onChanged;
    private readonly IReadOnlyList<TranscriptSegment> _segments;

    public ChatWindow(ISessionStore store, SessionSummary summary, Action onChanged)
    {
        InitializeComponent();
        _store = store;
        _onChanged = onChanged;

        var loaded = store.GetSession(summary.Id)!.Value;
        Summary = loaded.Session;
        _segments = loaded.Segments;

        Segments = [.. _segments.Select(s => new SegmentVm(s))];
        DataContext = this;
    }

    public SessionSummary Summary { get; private set; }

    public string SessionTitle
    {
        get => Summary.Title;
        set => Summary = Summary with { Title = value };
    }

    public string SessionTags
    {
        get => Summary.Tags;
        set => Summary = Summary with { Tags = value };
    }

    public string SessionDateText => $"{Summary.StartedAt:dd/MM/yyyy HH:mm} · {Summary.ActiveDuration:hh\\:mm\\:ss}";

    public List<SegmentVm> Segments { get; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _store.UpdateSession(Summary.Id, Summary.Title, Summary.Tags, Summary.Notes);
        _onChanged();
        System.Windows.MessageBox.Show(this, "Cambios guardados.", "Voice Recorder", MessageBoxButton.OK, MessageBoxImage.Information);
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
        _onChanged();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        _onChanged();
        base.OnClosing(e);
    }
}

