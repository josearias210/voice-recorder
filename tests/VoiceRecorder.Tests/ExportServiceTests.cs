using VoiceRecorder.App.Services;
using VoiceRecorder.Core;
using Xunit;

namespace VoiceRecorder.Tests;

public class ExportServiceTests
{
    private static readonly SessionSummary Session = new(
        Id: 1,
        Title: "Llamada equipo",
        StartedAt: new DateTime(2026, 9, 23, 10, 30, 0),
        ActiveDuration: TimeSpan.FromMinutes(3),
        Tags: "equipo, sprint",
        Notes: string.Empty,
        SegmentCount: 2);

    private static readonly List<TranscriptSegment> Segments =
    [
        new(Speaker.Me, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), "Hola, ¿me escuchas?"),
        new(Speaker.Them, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(9), "Perfectamente, adelante."),
    ];

    [Fact]
    public void Txt_Incluye_Hablantes_Y_Timestamps()
    {
        var content = ExportService.BuildContent(Session, Segments, ExportFormat.Txt);

        Assert.Contains("Llamada equipo", content);
        Assert.Contains("Etiquetas: equipo, sprint", content);
        Assert.Contains("[00:00:01] Yo: Hola, ¿me escuchas?", content);
        Assert.Contains("[00:00:05] Ellos: Perfectamente, adelante.", content);
    }

    [Fact]
    public void Markdown_Incluye_Cabecera_Y_Segmentos()
    {
        var content = ExportService.BuildContent(Session, Segments, ExportFormat.Markdown);

        Assert.Contains("# Llamada equipo", content);
        Assert.Contains("**[00:00:01] Yo**", content);
        Assert.Contains("Perfectamente, adelante.", content);
    }

    [Fact]
    public void Srt_Genera_Indices_Y_Tiempos()
    {
        var content = ExportService.BuildContent(Session, Segments, ExportFormat.Srt);
        var nl = Environment.NewLine;

        Assert.Contains($"1{nl}00:00:01,000 --> 00:00:04,000{nl}Yo: Hola, ¿me escuchas?", content);
        Assert.Contains($"2{nl}00:00:05,000 --> 00:00:09,000{nl}Ellos: Perfectamente, adelante.", content);
    }

    [Fact]
    public void BuildFileName_Sanea_Caracteres_Invalidos()
    {
        var session = Session with { Title = "Llamada: equipo/producción?" };
        var name = ExportService.BuildFileName(session, ExportFormat.Txt);

        Assert.EndsWith(".txt", name);
        Assert.DoesNotContain(":", name);
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("?", name);
    }
}
