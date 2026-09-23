using VoiceRecorder.Core;

namespace VoiceRecorder.App.ViewModels;

/// <summary>Burbuja del chat de transcripción.</summary>
public sealed class SegmentVm
{
    public SegmentVm(TranscriptSegment segment)
    {
        Source = segment;
        Speaker = segment.Speaker;
        TimeText = segment.Start.ToString(@"hh\:mm\:ss");
        Text = segment.Text;
    }

    public TranscriptSegment Source { get; }

    public Speaker Speaker { get; }

    public string TimeText { get; }

    public string Text { get; }

    public bool IsMe => Speaker == Speaker.Me;

    public bool IsThem => Speaker == Speaker.Them;

    public string SpeakerLabel => Speaker switch
    {
        Speaker.Me => "Yo",
        Speaker.Them => "Ellos",
        Speaker.Both => "Ambos",
        _ => "???",
    };
}
