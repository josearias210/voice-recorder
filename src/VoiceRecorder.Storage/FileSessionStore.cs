using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceRecorder.Core;

namespace VoiceRecorder.Storage;

/// <summary>
/// Almacén de sesiones en carpetas, sin base de datos. Cada sesión vive en
/// <c>{raíz}/{AAAA}/{MM}/{AAAAMMDD-HHmmss}-{id}/</c> con su transcripción en
/// <c>session.json</c> y sus archivos (WAV, etc.) al lado. La misma estructura
/// se replica tal cual en el respaldo externo.
/// </summary>
public sealed class FileSessionStore : ISessionFileStore
{
    /// <summary>Nombre del archivo de metadatos y transcripción de cada sesión.</summary>
    public const string SessionFileName = "session.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly Dictionary<long, string> _folders = [];
    private long _lastId;

    public FileSessionStore(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(RootDirectory);
        ScanFolders();
    }

    public string RootDirectory { get; }

    public event Action? Changed;

    /// <summary>Indica si todavía no hay ninguna sesión guardada.</summary>
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _folders.Count == 0;
            }
        }
    }

    public long SaveSession(
        string title,
        DateTime startedAt,
        TimeSpan activeDuration,
        string tags,
        string notes,
        IReadOnlyList<TranscriptSegment> segments)
    {
        long id;
        lock (_gate)
        {
            id = ++_lastId;
            WriteNew(id, title, startedAt, activeDuration, tags, notes, segments);
        }

        Changed?.Invoke();
        return id;
    }

    /// <summary>
    /// Importa una sesión existente (migración) conservando su Id si está libre.
    /// Devuelve el Id final.
    /// </summary>
    public long ImportSession(SessionSummary session, IReadOnlyList<TranscriptSegment> segments)
    {
        long id;
        lock (_gate)
        {
            id = session.Id > 0 && !_folders.ContainsKey(session.Id) ? session.Id : _lastId + 1;
            _lastId = Math.Max(_lastId, id);
            WriteNew(id, session.Title, session.StartedAt, session.ActiveDuration, session.Tags, session.Notes, segments);
        }

        Changed?.Invoke();
        return id;
    }

    public IReadOnlyList<SessionSummary> ListSessions(string? searchText = null)
    {
        var query = searchText?.Trim();
        var result = new List<SessionSummary>();
        foreach (var (id, folder) in SnapshotFolders())
        {
            var data = TryRead(folder);
            if (data is null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(query) && !Matches(data, query))
            {
                continue;
            }

            result.Add(ToSummary(id, data));
        }

        return result.OrderByDescending(s => s.StartedAt).ThenByDescending(s => s.Id).ToList();
    }

    public (SessionSummary Session, IReadOnlyList<TranscriptSegment> Segments)? GetSession(long id)
    {
        var folder = GetSessionFolder(id);
        if (folder is null || TryRead(folder) is not { } data)
        {
            return null;
        }

        var segments = (data.Segments ?? [])
            .Select(s => new TranscriptSegment(s.Speaker, TimeSpan.FromSeconds(s.Start), TimeSpan.FromSeconds(s.End), s.Text ?? string.Empty))
            .OrderBy(s => s.Start)
            .ToList();
        return (ToSummary(id, data) with { SegmentCount = 0 }, segments);
    }

    public void UpdateSession(long id, string title, string tags, string notes)
    {
        lock (_gate)
        {
            if (!_folders.TryGetValue(id, out var folder) || TryRead(folder) is not { } data)
            {
                return;
            }

            Write(folder, data with { Title = title ?? string.Empty, Tags = tags ?? string.Empty, Notes = notes ?? string.Empty });
        }

        Changed?.Invoke();
    }

    public void DeleteSession(long id)
    {
        lock (_gate)
        {
            if (!_folders.Remove(id, out var folder))
            {
                return;
            }

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            DeleteEmptyParents(folder);
        }
    }

    public string? GetSessionFolder(long id)
    {
        lock (_gate)
        {
            return _folders.TryGetValue(id, out var folder) ? folder : null;
        }
    }

    public IReadOnlyList<string> AttachFiles(long id, IReadOnlyList<string> sourcePaths)
    {
        var finalPaths = new List<string>(sourcePaths.Count);
        lock (_gate)
        {
            if (!_folders.TryGetValue(id, out var folder))
            {
                return sourcePaths;
            }

            var renames = new List<(string From, string To)>();
            foreach (var source in sourcePaths)
            {
                var target = UniquePath(folder, Path.GetFileName(source));
                try
                {
                    if (!File.Exists(source))
                    {
                        finalPaths.Add(source);
                        continue;
                    }

                    if (!string.Equals(Path.GetFullPath(source), target, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(source, target);
                        renames.Add((source, target));
                    }

                    finalPaths.Add(target);
                }
                catch (IOException)
                {
                    finalPaths.Add(source);
                }
                catch (UnauthorizedAccessException)
                {
                    finalPaths.Add(source);
                }
            }

            if (renames.Count > 0 && TryRead(folder) is { } data)
            {
                var notes = data.Notes ?? string.Empty;
                foreach (var (from, to) in renames)
                {
                    notes = notes.Replace(from, to, StringComparison.OrdinalIgnoreCase);
                }

                Write(folder, data with { Notes = notes });
            }
        }

        Changed?.Invoke();
        return finalPaths;
    }

    /// <summary>Ruta relativa (con '/') de la carpeta de una sesión dentro de la raíz.</summary>
    public static string BuildRelativeFolder(long id, DateTime startedAt) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{startedAt:yyyy}/{startedAt:MM}/{startedAt:yyyyMMdd-HHmmss}-{id:D5}");

    private void WriteNew(
        long id,
        string title,
        DateTime startedAt,
        TimeSpan activeDuration,
        string tags,
        string notes,
        IReadOnlyList<TranscriptSegment> segments)
    {
        var folder = Path.Combine(RootDirectory, BuildRelativeFolder(id, startedAt).Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folder);
        Write(folder, new SessionFile(
            1,
            id,
            title ?? string.Empty,
            startedAt,
            activeDuration.TotalSeconds,
            tags ?? string.Empty,
            notes ?? string.Empty,
            segments.Select(s => new SegmentFile(s.Speaker, s.Start.TotalSeconds, s.End.TotalSeconds, s.Text)).ToList()));
        _folders[id] = folder;
    }

    private List<KeyValuePair<long, string>> SnapshotFolders()
    {
        lock (_gate)
        {
            return [.. _folders];
        }
    }

    private void ScanFolders()
    {
        foreach (var file in Directory.EnumerateFiles(RootDirectory, SessionFileName, SearchOption.AllDirectories))
        {
            var folder = Path.GetDirectoryName(file)!;
            if (TryRead(folder) is not { } data || data.Id <= 0 || _folders.ContainsKey(data.Id))
            {
                continue;
            }

            _folders[data.Id] = folder;
            _lastId = Math.Max(_lastId, data.Id);
        }
    }

    private void DeleteEmptyParents(string folder)
    {
        var parent = Path.GetDirectoryName(folder);
        while (parent is not null
               && parent.Length > RootDirectory.Length
               && parent.StartsWith(RootDirectory, StringComparison.OrdinalIgnoreCase)
               && Directory.Exists(parent)
               && !Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent);
            parent = Path.GetDirectoryName(parent);
        }
    }

    private static string UniquePath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; File.Exists(candidate) || fileName == SessionFileName; i++)
        {
            candidate = Path.Combine(folder, $"{name}-{i}{ext}");
            fileName = Path.GetFileName(candidate);
        }

        return candidate;
    }

    private static bool Matches(SessionFile data, string query) =>
        Contains(data.Title, query)
        || Contains(data.Tags, query)
        || Contains(data.Notes, query)
        || (data.Segments ?? []).Any(s => Contains(s.Text, query));

    private static bool Contains(string? text, string query) =>
        text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;

    private static SessionSummary ToSummary(long id, SessionFile data) => new(
        id,
        data.Title ?? string.Empty,
        data.StartedAt,
        TimeSpan.FromSeconds(data.ActiveDurationSeconds),
        data.Tags ?? string.Empty,
        data.Notes ?? string.Empty,
        data.Segments?.Count ?? 0);

    private static SessionFile? TryRead(string folder)
    {
        try
        {
            var path = Path.Combine(folder, SessionFileName);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SessionFile>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Escritura atómica: archivo temporal y reemplazo.</summary>
    private static void Write(string folder, SessionFile data)
    {
        var path = Path.Combine(folder, SessionFileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Formato de <c>session.json</c>.</summary>
    internal sealed record SessionFile(
        int Version,
        long Id,
        string? Title,
        DateTime StartedAt,
        double ActiveDurationSeconds,
        string? Tags,
        string? Notes,
        List<SegmentFile>? Segments);

    /// <summary>Segmento de transcripción dentro de <c>session.json</c>.</summary>
    internal sealed record SegmentFile(Speaker Speaker, double Start, double End, string? Text);
}
