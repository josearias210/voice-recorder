using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VoiceRecorder.Core;

namespace VoiceRecorder.Storage.Notion;

/// <summary>Database properties the exporter fills, found by type and preferred name.</summary>
/// <param name="TitleProperty">Name of the title property (every database has one).</param>
/// <param name="DateProperty">Date property for the session start, if any.</param>
/// <param name="TagsProperty">Multi-select property for the tags, if any.</param>
/// <param name="DurationProperty">Number (minutes) or text property for the duration, if any.</param>
/// <param name="DurationIsNumber">True if <paramref name="DurationProperty"/> is a number.</param>
public sealed record NotionDatabaseInfo(
    string Title,
    string TitleProperty,
    string? DateProperty,
    string? TagsProperty,
    string? DurationProperty,
    bool DurationIsNumber);

/// <summary>
/// Uploads a session (summary and transcript) as a page of a Notion database, with the
/// user's own internal integration token. Nothing is sent unless the user confirms.
/// </summary>
public sealed partial class NotionExporter
{
    public const string ApiVersion = "2022-06-28";

    public static readonly Uri DefaultEndpoint = new("https://api.notion.com/v1/");

    /// <summary>Notion limit of characters per rich text object.</summary>
    internal const int MaxTextLength = 2000;

    /// <summary>Notion limit of blocks per request.</summary>
    internal const int MaxBlocksPerRequest = 100;

    private const int MaxAttempts = 4;

    private static readonly string[] DateNames = ["Fecha", "Date"];
    private static readonly string[] TagNames = ["Etiquetas", "Tags"];
    private static readonly string[] DurationNames = ["Duración", "Duracion", "Duration"];

    private readonly HttpClient _http;
    private readonly string _token;
    private readonly Uri _endpoint;

    public NotionExporter(HttpClient http, string token, string databaseId, Uri? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Falta el token de la integración de Notion.", nameof(token));
        }

        DatabaseId = ParseId(databaseId)
            ?? throw new ArgumentException("El identificador de la base de datos de Notion no es válido: pega el enlace o el Id de la base de datos.", nameof(databaseId));
        _http = http;
        _token = token.Trim();
        _endpoint = endpoint ?? DefaultEndpoint;
    }

    /// <summary>Database Id in canonical form (8-4-4-4-12).</summary>
    public string DatabaseId { get; }

    /// <summary>Base delay between retries (configurable in tests).</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Extracts the 32-hex-digit Id from a Notion link or Id (with or without dashes).
    /// Returns null if there is none.
    /// </summary>
    public static string? ParseId(string? linkOrId)
    {
        if (string.IsNullOrWhiteSpace(linkOrId))
        {
            return null;
        }

        var text = linkOrId.Trim();
        var query = text.IndexOfAny(['?', '#']);
        if (query >= 0)
        {
            text = text[..query];
        }

        var match = IdPattern().Match(text.Replace("-", string.Empty, StringComparison.Ordinal));
        if (!match.Success)
        {
            return null;
        }

        var hex = match.Value.ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    /// <summary>Reads the database and the properties to fill. Throws with a readable reason if it is not reachable.</summary>
    public async Task<NotionDatabaseInfo> GetDatabaseInfoAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"databases/{DatabaseId}", null, cancellationToken);
        var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?.AsObject()
            ?? throw new InvalidOperationException("Notion devolvió una respuesta vacía.");
        return ParseDatabaseInfo(root);
    }

    internal static NotionDatabaseInfo ParseDatabaseInfo(JsonObject root)
    {
        var title = string.Concat((root["title"] as JsonArray ?? [])
            .Select(t => t?["plain_text"]?.GetValue<string>() ?? string.Empty));
        var properties = (root["properties"] as JsonObject ?? [])
            .Select(p => (Name: p.Key, Type: p.Value?["type"]?.GetValue<string>() ?? string.Empty))
            .ToList();

        var titleProperty = properties.FirstOrDefault(p => p.Type == "title").Name
            ?? throw new InvalidOperationException("La base de datos de Notion no tiene propiedad de título.");

        string? Find(string[] names, params string[] types) =>
            names.Select(n => properties.FirstOrDefault(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase) && types.Contains(p.Type)).Name)
                 .FirstOrDefault(n => n is not null)
            ?? (types.Length == 1 && types[0] is "date" or "multi_select"
                ? properties.FirstOrDefault(p => p.Type == types[0]).Name
                : null);

        var date = Find(DateNames, "date");
        var tags = Find(TagNames, "multi_select");
        var duration = Find(DurationNames, "number", "rich_text");
        var durationIsNumber = duration is not null && properties.First(p => p.Name == duration).Type == "number";
        return new NotionDatabaseInfo(string.IsNullOrWhiteSpace(title) ? "(sin título)" : title, titleProperty, date, tags, duration, durationIsNumber);
    }

    /// <summary>
    /// Creates a page for the session in the database: summary (if any) and the transcript
    /// inside a toggle heading. Returns where it was created.
    /// </summary>
    public async Task<NotionExport> ExportAsync(
        SessionSummary session,
        IReadOnlyList<TranscriptSegment> segments,
        NotionContent content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(segments);
        if (WhatWillBeUploaded(session, segments, content) == NotionContent.None)
        {
            throw new InvalidOperationException("La sesión no tiene nada de lo que eliges subir (resumen, transcripción o análisis).");
        }

        var info = await GetDatabaseInfoAsync(cancellationToken);

        var body = new JsonObject
        {
            ["parent"] = new JsonObject { ["database_id"] = DatabaseId },
            ["properties"] = BuildProperties(session, info),
        };
        var headerBlocks = BuildHeaderBlocks(session, content);
        body["children"] = new JsonArray([.. headerBlocks.Take(MaxBlocksPerRequest)]);

        using var created = await SendAsync(HttpMethod.Post, "pages", body, cancellationToken);
        var page = JsonNode.Parse(await created.Content.ReadAsStringAsync(cancellationToken))!;
        var pageId = page["id"]?.GetValue<string>() ?? throw new InvalidOperationException("Notion no devolvió el Id de la página.");
        var url = page["url"]?.GetValue<string>() ?? $"https://www.notion.so/{pageId.Replace("-", string.Empty, StringComparison.Ordinal)}";

        await AppendBlocksAsync(pageId, headerBlocks.Skip(MaxBlocksPerRequest).ToList(), cancellationToken);

        if (content.HasFlag(NotionContent.Review) && session.Review is { } review)
        {
            await AppendToggleAsync(pageId, "Análisis de comunicación", MarkdownToBlocks(review.Text, demoteHeadings: true), cancellationToken);
        }

        if (content.HasFlag(NotionContent.Transcript))
        {
            await AppendToggleAsync(pageId, "Transcripción", BuildTranscriptBlocks(segments), cancellationToken);
        }

        return new NotionExport(pageId, url, DateTime.Now);
    }

    /// <summary>Moves a previously uploaded page to the trash (when uploading the session again).</summary>
    public async Task ArchivePageAsync(string pageId, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(HttpMethod.Patch, $"pages/{pageId}", new JsonObject { ["archived"] = true }, cancellationToken);
    }

    internal static JsonObject BuildProperties(SessionSummary session, NotionDatabaseInfo info)
    {
        var properties = new JsonObject
        {
            [info.TitleProperty] = new JsonObject { ["title"] = RichText(string.IsNullOrWhiteSpace(session.Title) ? "Sesión" : session.Title) },
        };

        if (info.DateProperty is { } date)
        {
            properties[date] = new JsonObject
            {
                ["date"] = new JsonObject
                {
                    ["start"] = session.StartedAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                },
            };
        }

        if (info.TagsProperty is { } tagsProperty)
        {
            var tags = session.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                // Notion limits option names to 100 characters.
                .Select(t => t.Length > 100 ? t[..100] : t)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(t => (JsonNode)new JsonObject { ["name"] = t });
            properties[tagsProperty] = new JsonObject { ["multi_select"] = new JsonArray([.. tags]) };
        }

        if (info.DurationProperty is { } duration)
        {
            properties[duration] = info.DurationIsNumber
                ? new JsonObject { ["number"] = Math.Round(session.ActiveDuration.TotalMinutes, 1) }
                : new JsonObject { ["rich_text"] = RichText(session.ActiveDuration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)) };
        }

        return properties;
    }

    /// <summary>Blocks before the transcript: notes and the summary converted from Markdown.</summary>
    /// <summary>Parts of <paramref name="content"/> the session actually has.</summary>
    public static NotionContent WhatWillBeUploaded(SessionSummary session, IReadOnlyList<TranscriptSegment> segments, NotionContent content)
    {
        var result = NotionContent.None;
        if (content.HasFlag(NotionContent.Summary) && session.CallSummary is not null)
        {
            result |= NotionContent.Summary;
        }

        if (content.HasFlag(NotionContent.Transcript) && segments.Any(s => !string.IsNullOrWhiteSpace(s.Text)))
        {
            result |= NotionContent.Transcript;
        }

        if (content.HasFlag(NotionContent.Review) && session.Review is not null)
        {
            result |= NotionContent.Review;
        }

        return result;
    }

    internal static List<JsonObject> BuildHeaderBlocks(SessionSummary session, NotionContent content = NotionContent.Summary)
    {
        var blocks = new List<JsonObject>();
        if (!string.IsNullOrWhiteSpace(session.Notes))
        {
            blocks.Add(Block("callout", RichText(session.Notes.Trim()), extra: o => o["icon"] = new JsonObject { ["type"] = "emoji", ["emoji"] = "📝" }));
        }

        if (content.HasFlag(NotionContent.Summary) && session.CallSummary is { } summary)
        {
            blocks.AddRange(MarkdownToBlocks(summary.Text));
        }

        return blocks;
    }

    /// <summary>
    /// Converts the summary's simple Markdown (headings, bullets, tasks, numbered lists and
    /// paragraphs) into Notion blocks. Bold markers are removed.
    /// </summary>
    internal static List<JsonObject> MarkdownToBlocks(string markdown, bool demoteHeadings = false)
    {
        var blocks = new List<JsonObject>();
        foreach (var rawLine in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                blocks.Add(Block("heading_3", RichText(Clean(trimmed[4..]))));
            }
            else if (trimmed.StartsWith("## ", StringComparison.Ordinal) || trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                blocks.Add(Block(demoteHeadings ? "heading_3" : "heading_2", RichText(Clean(trimmed[(trimmed.IndexOf(' ') + 1)..]))));
            }
            else if (TaskPattern().Match(trimmed) is { Success: true } task)
            {
                var done = task.Groups[1].Value is "x" or "X";
                blocks.Add(Block("to_do", RichText(Clean(task.Groups[2].Value)), extra: o => o["checked"] = done));
            }
            else if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                blocks.Add(Block("bulleted_list_item", RichText(Clean(trimmed[2..]))));
            }
            else if (NumberedPattern().Match(trimmed) is { Success: true } numbered)
            {
                blocks.Add(Block("numbered_list_item", RichText(Clean(numbered.Groups[1].Value))));
            }
            else
            {
                blocks.Add(Block("paragraph", RichText(Clean(trimmed))));
            }
        }

        return blocks;
    }

    /// <summary>One paragraph per segment: "[00:01:05] Ellos: text", with the speaker in bold.</summary>
    internal static List<JsonObject> BuildTranscriptBlocks(IReadOnlyList<TranscriptSegment> segments)
    {
        var blocks = new List<JsonObject>();
        foreach (var segment in segments.OrderBy(s => s.Start))
        {
            if (string.IsNullOrWhiteSpace(segment.Text))
            {
                continue;
            }

            var prefix = $"[{segment.Start.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)}] {SpeakerLabel(segment.Speaker)}: ";
            var richText = new JsonArray(TextObject(prefix, bold: true));
            foreach (var part in RichText(segment.Text.Trim()))
            {
                richText.Add(part!.DeepClone());
            }

            blocks.Add(Block("paragraph", richText));
        }

        return blocks;
    }

    /// <summary>Text split into rich text objects within Notion's per-object length limit.</summary>
    internal static JsonArray RichText(string text)
    {
        var array = new JsonArray();
        for (var i = 0; i < text.Length; i += MaxTextLength)
        {
            array.Add(TextObject(text.Substring(i, Math.Min(MaxTextLength, text.Length - i)), bold: false));
        }

        return array;
    }

    private static string SpeakerLabel(Speaker speaker) => speaker switch
    {
        Speaker.Me => "Yo",
        Speaker.Them => "Ellos",
        Speaker.Both => "Ambos",
        _ => "Audio",
    };

    private static JsonObject TextObject(string content, bool bold)
    {
        var text = new JsonObject
        {
            ["type"] = "text",
            ["text"] = new JsonObject { ["content"] = content },
        };
        if (bold)
        {
            text["annotations"] = new JsonObject { ["bold"] = true };
        }

        return text;
    }

    private static JsonObject Block(string type, JsonArray richText, Action<JsonObject>? extra = null)
    {
        var content = new JsonObject { ["rich_text"] = richText };
        extra?.Invoke(content);
        return new JsonObject { ["object"] = "block", ["type"] = type, [type] = content };
    }

    private static string Clean(string text) => text.Replace("**", string.Empty, StringComparison.Ordinal).Trim();

    /// <summary>Appends a toggle heading to the page and puts <paramref name="blocks"/> inside it, in batches.</summary>
    private async Task AppendToggleAsync(string pageId, string title, IReadOnlyList<JsonObject> blocks, CancellationToken cancellationToken)
    {
        if (blocks.Count == 0)
        {
            return;
        }

        var toggle = new JsonObject
        {
            ["object"] = "block",
            ["type"] = "heading_2",
            ["heading_2"] = new JsonObject
            {
                ["rich_text"] = RichText(title),
                ["is_toggleable"] = true,
            },
        };
        using var appended = await SendAsync(
            HttpMethod.Patch,
            $"blocks/{pageId}/children",
            new JsonObject { ["children"] = new JsonArray(toggle) },
            cancellationToken);
        var result = JsonNode.Parse(await appended.Content.ReadAsStringAsync(cancellationToken));
        var toggleId = (result?["results"] as JsonArray)?.FirstOrDefault()?["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Notion no devolvió el bloque «{title}».");
        await AppendBlocksAsync(toggleId, blocks, cancellationToken);
    }

    private async Task AppendBlocksAsync(string parentId, IReadOnlyList<JsonObject> blocks, CancellationToken cancellationToken)
    {
        for (var i = 0; i < blocks.Count; i += MaxBlocksPerRequest)
        {
            var chunk = new JsonArray([.. blocks.Skip(i).Take(MaxBlocksPerRequest).Select(b => (JsonNode)b.DeepClone())]);
            using var _ = await SendAsync(HttpMethod.Patch, $"blocks/{parentId}/children", new JsonObject { ["children"] = chunk }, cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        var payload = body?.ToJsonString();
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, new Uri(_endpoint, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            request.Headers.Add("Notion-Version", ApiVersion);
            if (payload is not null)
            {
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            }

            var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            using (response)
            {
                var retryable = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Conflict || (int)response.StatusCode >= 500;
                if (!retryable || attempt >= MaxAttempts)
                {
                    throw new InvalidOperationException(await DescribeErrorAsync(response, cancellationToken));
                }

                var delay = response.Headers.RetryAfter?.Delta ?? RetryDelay * attempt;
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>User-readable message from a Notion error response.</summary>
    internal static async Task<string> DescribeErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? apiMessage = null;
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (json.RootElement.TryGetProperty("message", out var message))
            {
                apiMessage = message.GetString();
            }
        }
        catch (JsonException)
        {
            // Body without JSON: the status code is enough.
        }

        var reason = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "El token de Notion no es válido o se ha revocado",
            HttpStatusCode.Forbidden => "La integración de Notion no tiene permiso para escribir en esa base de datos",
            HttpStatusCode.NotFound => "Notion no encuentra la base de datos: comprueba el enlace y compártela con tu integración (… › Conexiones)",
            HttpStatusCode.TooManyRequests => "Notion rechaza más peticiones por ahora (límite de uso)",
            HttpStatusCode.BadRequest => "Notion rechazó los datos de la página",
            _ => $"Notion respondió {(int)response.StatusCode}",
        };
        return string.IsNullOrWhiteSpace(apiMessage) ? $"{reason}." : $"{reason}: {apiMessage}";
    }

    [GeneratedRegex("[0-9a-fA-F]{32}(?![0-9a-fA-F])")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[-*] \[([ xX])\] (.*)$")]
    private static partial Regex TaskPattern();

    [GeneratedRegex(@"^\d+[.)] (.*)$")]
    private static partial Regex NumberedPattern();
}
