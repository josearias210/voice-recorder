using System.Globalization;
using System.Text;
using System.Text.Json;
using VoiceRecorder.Core;

namespace VoiceRecorder.Storage;

/// <summary>Resumen de un mes de transcripciones.</summary>
/// <param name="Month">Primer día del mes.</param>
/// <param name="Runs">Número de transcripciones.</param>
/// <param name="PaidRuns">Transcripciones con un motor de pago.</param>
/// <param name="AudioSeconds">Audio procesado en total.</param>
/// <param name="PaidAudioSeconds">Audio enviado a motores de pago.</param>
/// <param name="Elapsed">Tiempo total de transcripción.</param>
/// <param name="CostUsd">Coste estimado del mes.</param>
public sealed record MonthlyTranscriptionSummary(
    DateTime Month,
    int Runs,
    int PaidRuns,
    double AudioSeconds,
    double PaidAudioSeconds,
    TimeSpan Elapsed,
    double CostUsd);

/// <summary>
/// Registro local de transcripciones (<c>transcripciones.jsonl</c>, una línea JSON
/// por transcripción). Es independiente de las sesiones: aunque se borre una
/// sesión, su coste sigue contando en el mes en que se hizo.
/// </summary>
public sealed class TranscriptionLog
{
    public const string FileName = "transcripciones.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly object _gate = new();

    public TranscriptionLog(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        FilePath = Path.Join(dataDirectory, FileName);
    }

    public string FilePath { get; }

    /// <summary>Añade una transcripción al registro.</summary>
    public void Append(TranscriptionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var line = JsonSerializer.Serialize(run, JsonOptions) + "\n";
        lock (_gate)
        {
            File.AppendAllText(FilePath, line, Encoding.UTF8);
        }
    }

    /// <summary>Todas las transcripciones registradas, de la más reciente a la más antigua. Ignora líneas dañadas.</summary>
    public IReadOnlyList<TranscriptionRun> ReadAll()
    {
        string[] lines;
        lock (_gate)
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            lines = File.ReadAllLines(FilePath, Encoding.UTF8);
        }

        var runs = new List<TranscriptionRun>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<TranscriptionRun>(line, JsonOptions) is { } run)
                {
                    runs.Add(run);
                }
            }
            catch (JsonException)
            {
                // Línea dañada (p. ej. corte de luz a mitad de escritura): se omite.
            }
        }

        return runs.OrderByDescending(r => r.StartedAt).ToList();
    }

    /// <summary>Resumen por mes, del más reciente al más antiguo.</summary>
    public static IReadOnlyList<MonthlyTranscriptionSummary> SummarizeByMonth(IEnumerable<TranscriptionRun> runs) =>
        runs.GroupBy(r => new DateTime(r.StartedAt.Year, r.StartedAt.Month, 1, 0, 0, 0, DateTimeKind.Unspecified))
            .Select(g => new MonthlyTranscriptionSummary(
                g.Key,
                g.Count(),
                g.Count(r => r.IsPaid),
                g.Sum(r => r.AudioSeconds),
                g.Where(r => r.IsPaid).Sum(r => r.AudioSeconds),
                TimeSpan.FromTicks(g.Sum(r => r.Elapsed.Ticks)),
                g.Sum(r => r.CostUsd)))
            .OrderByDescending(m => m.Month)
            .ToList();

    private static readonly string[] MonthNames =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
    ];

    /// <summary>Nombre del mes para la UI (p. ej. "noviembre de 2026"), sin depender de la configuración regional.</summary>
    public static string FormatMonth(DateTime month) =>
        string.Create(CultureInfo.InvariantCulture, $"{MonthNames[month.Month - 1]} de {month.Year}");
}
