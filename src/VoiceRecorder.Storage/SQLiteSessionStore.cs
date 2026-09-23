using Microsoft.Data.Sqlite;
using VoiceRecorder.Core;

namespace VoiceRecorder.Storage;

/// <summary>Implementación SQLite de <see cref="ISessionStore"/>.</summary>
public sealed class SQLiteSessionStore : ISessionStore, IDisposable
{
    private readonly string _connectionString;

    public SQLiteSessionStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
        Initialize();
    }

    public long SaveSession(
        string title,
        DateTime startedAt,
        TimeSpan activeDuration,
        string tags,
        string notes,
        IReadOnlyList<TranscriptSegment> segments)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        var sessionId = 0L;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Sessions (Title, StartedAt, ActiveDurationSeconds, Tags, Notes)
                VALUES ($title, $startedAt, $duration, $tags, $notes);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$startedAt", startedAt.ToString("O"));
            command.Parameters.AddWithValue("$duration", activeDuration.TotalSeconds);
            command.Parameters.AddWithValue("$tags", tags ?? string.Empty);
            command.Parameters.AddWithValue("$notes", notes ?? string.Empty);
            sessionId = (long)command.ExecuteScalar()!;
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Segments (SessionId, Speaker, StartSeconds, EndSeconds, Text)
                VALUES ($sessionId, $speaker, $start, $end, $text);
                """;
            var pSession = command.Parameters.Add("$sessionId", SqliteType.Integer);
            var pSpeaker = command.Parameters.Add("$speaker", SqliteType.Integer);
            var pStart = command.Parameters.Add("$start", SqliteType.Real);
            var pEnd = command.Parameters.Add("$end", SqliteType.Real);
            var pText = command.Parameters.Add("$text", SqliteType.Text);

            foreach (var segment in segments)
            {
                pSession.Value = sessionId;
                pSpeaker.Value = (long)segment.Speaker;
                pStart.Value = segment.Start.TotalSeconds;
                pEnd.Value = segment.End.TotalSeconds;
                pText.Value = segment.Text;
                command.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        return sessionId;
    }

    public IReadOnlyList<SessionSummary> ListSessions(string? searchText = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.Title, s.StartedAt, s.ActiveDurationSeconds, s.Tags, s.Notes,
                   (SELECT COUNT(*) FROM Segments seg WHERE seg.SessionId = s.Id) AS SegmentCount
            FROM Sessions s
            """;
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            command.CommandText += """
                 WHERE s.Title LIKE $q OR s.Tags LIKE $q OR s.Notes LIKE $q
                    OR EXISTS (SELECT 1 FROM Segments seg WHERE seg.SessionId = s.Id AND seg.Text LIKE $q)
                """;
            command.Parameters.AddWithValue("$q", $"%{searchText.Trim()}%");
        }

        command.CommandText += " ORDER BY s.StartedAt DESC";

        var result = new List<SessionSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new SessionSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2)),
                TimeSpan.FromSeconds(reader.GetDouble(3)),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6)));
        }

        return result;
    }

    public (SessionSummary Session, IReadOnlyList<TranscriptSegment> Segments)? GetSession(long id)
    {
        SessionSummary? session = null;
        var segments = new List<TranscriptSegment>();

        using (var connection = Open())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT Id, Title, StartedAt, ActiveDurationSeconds, Tags, Notes
                    FROM Sessions WHERE Id = $id
                    """;
                command.Parameters.AddWithValue("$id", id);
                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return null;
                }

                session = new SessionSummary(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    DateTime.Parse(reader.GetString(2)),
                    TimeSpan.FromSeconds(reader.GetDouble(3)),
                    reader.GetString(4),
                    reader.GetString(5),
                    0);
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT Speaker, StartSeconds, EndSeconds, Text
                    FROM Segments WHERE SessionId = $id ORDER BY StartSeconds
                    """;
                command.Parameters.AddWithValue("$id", id);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    segments.Add(new TranscriptSegment(
                        (Speaker)reader.GetInt64(0),
                        TimeSpan.FromSeconds(reader.GetDouble(1)),
                        TimeSpan.FromSeconds(reader.GetDouble(2)),
                        reader.GetString(3)));
                }
            }
        }

        return (session!, segments);
    }

    public void UpdateSession(long id, string title, string tags, string notes)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Sessions SET Title = $title, Tags = $tags, Notes = $notes WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$title", title ?? string.Empty);
        command.Parameters.AddWithValue("$tags", tags ?? string.Empty);
        command.Parameters.AddWithValue("$notes", notes ?? string.Empty);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void DeleteSession(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Segments WHERE SessionId = $id; DELETE FROM Sessions WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS Sessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL DEFAULT '',
                StartedAt TEXT NOT NULL,
                ActiveDurationSeconds REAL NOT NULL DEFAULT 0,
                Tags TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS Segments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId INTEGER NOT NULL REFERENCES Sessions(Id) ON DELETE CASCADE,
                Speaker INTEGER NOT NULL,
                StartSeconds REAL NOT NULL,
                EndSeconds REAL NOT NULL,
                Text TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Segments_Session ON Segments(SessionId);
            """;
        command.ExecuteNonQuery();
    }
}
