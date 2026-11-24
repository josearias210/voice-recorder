using VoiceRecorder.Core;

namespace VoiceRecorder.App.ViewModels;

/// <summary>Fila del historial de sesiones.</summary>
public sealed class SessionRowVm
{
    public SessionRowVm(SessionSummary summary)
    {
        Summary = summary;
    }

    public SessionSummary Summary { get; }

    public long Id => Summary.Id;

    public string Title => string.IsNullOrWhiteSpace(Summary.Title) ? "(sin título)" : Summary.Title;

    public string StartedAtText => Summary.StartedAt.ToString("dd/MM/yyyy HH:mm");

    public string DurationText => Summary.ActiveDuration.ToString(@"hh\:mm\:ss");

    public string TagsText => string.IsNullOrWhiteSpace(Summary.Tags) ? "—" : Summary.Tags;

    public string SegmentCountText => Summary.SegmentCount > 0 ? $"{Summary.SegmentCount} segmentos" : "sin transcribir";

    /// <summary>Última transcripción: "OpenAI · 3 min 20 s · ≈ 0,014 USD" (vacío si no hay datos).</summary>
    public string TranscriptionText => Summary.LastTranscription is { } run
        ? $"🧾 {Services.CostFormat.Summary(run)}"
        : string.Empty;

    public bool HasTranscriptionInfo => Summary.LastTranscription is not null;
}
