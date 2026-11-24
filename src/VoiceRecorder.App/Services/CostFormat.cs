using System.Globalization;
using VoiceRecorder.Core;

namespace VoiceRecorder.App.Services;

/// <summary>Textos de tiempo y coste de las transcripciones para la UI.</summary>
public static class CostFormat
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>"45 s", "3 min 20 s", "1 h 05 min".</summary>
    public static string Duration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalHours >= 1)
        {
            return string.Format(Spanish, "{0} h {1:D2} min", (int)duration.TotalHours, duration.Minutes);
        }

        return duration.TotalMinutes >= 1
            ? string.Format(Spanish, "{0} min {1:D2} s", (int)duration.TotalMinutes, duration.Seconds)
            : string.Format(Spanish, "{0} s", Math.Max(0, (int)Math.Round(duration.TotalSeconds)));
    }

    /// <summary>"gratis" o "≈ 0,014 USD" (más decimales cuanto menor es el importe).</summary>
    public static string Cost(double usd)
    {
        if (usd <= 0)
        {
            return "gratis";
        }

        var format = usd < 0.01 ? "0.0000" : usd < 1 ? "0.000" : "0.00";
        return $"≈ {usd.ToString(format, Spanish)} USD";
    }

    /// <summary>Minutos de audio con un decimal ("4,2 min").</summary>
    public static string AudioMinutes(double seconds) =>
        string.Format(Spanish, "{0:F1} min", seconds / 60);

    /// <summary>Resumen corto de una transcripción: "OpenAI · 3 min 20 s · ≈ 0,014 USD".</summary>
    public static string Summary(TranscriptionRun run) =>
        $"{run.Engine} · {Duration(run.Elapsed)} · {Cost(run.CostUsd)}";
}
