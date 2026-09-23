using System.IO;
using System.Text;
using VoiceRecorder.Core;

namespace VoiceRecorder.App.Services;

/// <summary>Exporta sesiones a TXT, Markdown o SRT.</summary>
public enum ExportFormat
{
    Txt,
    Markdown,
    Srt,
}

public static class ExportService
{
    public static string BuildFileName(SessionSummary session, ExportFormat format) => format switch
    {
        ExportFormat.Txt => $"{Sanitize(session.Title)}.txt",
        ExportFormat.Markdown => $"{Sanitize(session.Title)}.md",
        ExportFormat.Srt => $"{Sanitize(session.Title)}.srt",
        _ => "sesion.txt",
    };

    public static string BuildContent(SessionSummary session, IReadOnlyList<TranscriptSegment> segments, ExportFormat format)
    {
        return format switch
        {
            ExportFormat.Txt => BuildTxt(session, segments),
            ExportFormat.Markdown => BuildMarkdown(session, segments),
            ExportFormat.Srt => BuildSrt(segments),
            _ => BuildTxt(session, segments),
        };
    }

    private static string BuildTxt(SessionSummary session, IReadOnlyList<TranscriptSegment> segments)
    {
        var sb = new StringBuilder();
        sb.AppendLine(session.Title);
        sb.AppendLine($"Fecha: {session.StartedAt:dd/MM/yyyy HH:mm}  ·  Duración activa: {session.ActiveDuration:hh\\:mm\\:ss}");
        if (!string.IsNullOrWhiteSpace(session.Tags))
        {
            sb.AppendLine($"Etiquetas: {session.Tags}");
        }

        sb.AppendLine(new string('─', 60));
        foreach (var segment in segments)
        {
            sb.AppendLine($"[{segment.Start:hh\\:mm\\:ss}] {Label(segment.Speaker)}: {segment.Text}");
        }

        return sb.ToString();
    }

    private static string BuildMarkdown(SessionSummary session, IReadOnlyList<TranscriptSegment> segments)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {session.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Fecha:** {session.StartedAt:dd/MM/yyyy HH:mm}  ");
        sb.AppendLine($"**Duración activa:** {session.ActiveDuration:hh\\:mm\\:ss}");
        if (!string.IsNullOrWhiteSpace(session.Tags))
        {
            sb.AppendLine($"**Etiquetas:** {session.Tags}");
        }

        sb.AppendLine();
        foreach (var segment in segments)
        {
            sb.AppendLine($"**[{segment.Start:hh\\:mm\\:ss}] {Label(segment.Speaker)}** — {segment.Text}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildSrt(IReadOnlyList<TranscriptSegment> segments)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            sb.AppendLine((i + 1).ToString());
            sb.AppendLine($"{SrtTime(segment.Start)} --> {SrtTime(segment.End)}");
            sb.AppendLine($"{Label(segment.Speaker)}: {segment.Text}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string SrtTime(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";

    private static string Label(Speaker speaker) => speaker switch
    {
        Speaker.Me => "Yo",
        Speaker.Them => "Ellos",
        Speaker.Both => "Ambos",
        _ => "???",
    };

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "sesion" : name.Trim();
    }
}
