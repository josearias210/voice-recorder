namespace VoiceRecorder.Storage.Notion;

/// <summary>Parts of a session that are uploaded to Notion (title, date, tags and notes always go).</summary>
[Flags]
public enum NotionContent
{
    None = 0,

    /// <summary>The AI summary.</summary>
    Summary = 1,

    /// <summary>The full transcript, inside a toggle heading.</summary>
    Transcript = 2,

    /// <summary>The experimental communication review, inside a toggle heading.</summary>
    Review = 4,
}
