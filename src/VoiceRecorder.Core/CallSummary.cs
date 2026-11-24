namespace VoiceRecorder.Core;

/// <summary>AI-generated text about a session: its summary, or its communication review.</summary>
/// <param name="Text">Summary in Markdown (headings "## ", bullets "- " and tasks "- [ ] ").</param>
/// <param name="CreatedAt">Local date and time when it was generated.</param>
/// <param name="Model">Model that generated it (e.g. "gpt-5-mini").</param>
/// <param name="CostUsd">Estimated cost in USD.</param>
public sealed record CallSummary(string Text, DateTime CreatedAt, string Model, double CostUsd);

/// <summary>Where a session was uploaded in Notion.</summary>
/// <param name="PageId">Id of the Notion page created for the session.</param>
/// <param name="Url">Link to the page.</param>
/// <param name="ExportedAt">Local date and time of the upload.</param>
public sealed record NotionExport(string PageId, string Url, DateTime ExportedAt);
