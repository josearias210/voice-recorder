using System.Diagnostics;
using System.Globalization;
using System.Windows;
using VoiceRecorder.App.Services;
using VoiceRecorder.Core;
using VoiceRecorder.Storage;

namespace VoiceRecorder.App;

/// <summary>Costes y tiempos de transcripción: resumen del mes, histórico por mes y detalle de cada transcripción.</summary>
public partial class CostsWindow : Window
{
    private const string OpenAiUsageUrl = "https://platform.openai.com/usage";

    private readonly IReadOnlyList<TranscriptionRun> _runs;

    public CostsWindow(TranscriptionLog log)
    {
        InitializeComponent();
        _runs = log.ReadAll();
        var months = TranscriptionLog.SummarizeByMonth(_runs);

        var now = DateTime.Now;
        var current = months.FirstOrDefault(m => m.Month.Year == now.Year && m.Month.Month == now.Month);
        MonthCostText.Text = CostFormat.Cost(current?.CostUsd ?? 0);
        MonthRunsText.Text = (current?.Runs ?? 0).ToString(CultureInfo.CurrentCulture);
        MonthTimeText.Text = CostFormat.Duration(current?.Elapsed ?? TimeSpan.Zero);
        TotalCostText.Text = CostFormat.Cost(_runs.Sum(r => r.CostUsd));

        EmptyText.Visibility = months.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MonthsList.ItemsSource = months.Select(m => new MonthRow(m)).ToList();
        // Selecting the first month fills the detail; with no months there is nothing to select.
        MonthsList.SelectedIndex = months.Count > 0 ? 0 : -1;
        if (months.Count == 0)
        {
            DetailHeader.Text = "Transcripciones";
        }
    }

    private void MonthsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MonthsList.SelectedItem is not MonthRow month)
        {
            RunsList.ItemsSource = null;
            return;
        }

        DetailHeader.Text = $"Transcripciones de {month.Name}";
        RunsList.ItemsSource = _runs
            .Where(r => r.StartedAt.Year == month.Month.Year && r.StartedAt.Month == month.Month.Month)
            .Select(r => new RunRow(r))
            .ToList();
    }

    private void OpenAiUsage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(OpenAiUsageUrl) { UseShellExecute = true });
        }
        catch
        {
            // Sin navegador predeterminado: el enlace queda en la ayuda.
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Fila de la lista de meses.</summary>
    private sealed class MonthRow(MonthlyTranscriptionSummary summary)
    {
        public DateTime Month => summary.Month;

        public string Name => TranscriptionLog.FormatMonth(summary.Month);

        public string Cost => CostFormat.Cost(summary.CostUsd);

        public string Detail
        {
            get
            {
                var free = summary.Runs - summary.PaidRuns;
                var parts = new List<string> { $"{summary.Runs} transcripciones · {CostFormat.Duration(summary.Elapsed)}" };
                if (summary.PaidRuns > 0)
                {
                    parts.Add($"OpenAI: {summary.PaidRuns} ({CostFormat.AudioMinutes(summary.PaidAudioSeconds)} enviados)");
                }

                if (free > 0)
                {
                    parts.Add($"Whisper: {free} (gratis)");
                }

                return string.Join("\n", parts);
            }
        }
    }

    /// <summary>Fila del detalle de transcripciones.</summary>
    private sealed class RunRow(TranscriptionRun run)
    {
        public string Date => run.StartedAt.ToString("dd/MM HH:mm", CultureInfo.CurrentCulture);

        public string Session => string.IsNullOrWhiteSpace(run.SessionTitle) ? "(sin título)" : run.SessionTitle!;

        public string Engine => run.Completed ? $"{run.Engine} · {run.Model}" : $"{run.Engine} · {run.Model} (incompleta)";

        public string Audio => CostFormat.AudioMinutes(run.AudioSeconds);

        public string Time => CostFormat.Duration(run.Elapsed);

        public string Cost => CostFormat.Cost(run.CostUsd);
    }
}
