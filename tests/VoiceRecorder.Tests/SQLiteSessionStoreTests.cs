using VoiceRecorder.Core;
using VoiceRecorder.Storage;
using Xunit;

namespace VoiceRecorder.Tests;

public sealed class SQLiteSessionStoreTests : IDisposable
{
    private readonly SQLiteSessionStore _store;
    private readonly string _dbPath;

    public SQLiteSessionStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"vr-tests-{Guid.NewGuid():N}.db");
        _store = new SQLiteSessionStore(_dbPath);
    }

    [Fact]
    public void SaveAndGet_Roundtrip_Conserva_Segmentos_Ordenados()
    {
        var segments = new List<TranscriptSegment>
        {
            new(Speaker.Them, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8), "Segundo mensaje"),
            new(Speaker.Me, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "Primer mensaje"),
        };

        var id = _store.SaveSession("Sesión test", new DateTime(2026, 9, 23, 9, 0, 0), TimeSpan.FromMinutes(2), "qa, pipeline", "notas", segments);

        var loaded = _store.GetSession(id)!;
        Assert.Equal("Sesión test", loaded.Value.Session.Title);
        Assert.Equal(TimeSpan.FromMinutes(2), loaded.Value.Session.ActiveDuration);
        Assert.Equal("qa, pipeline", loaded.Value.Session.Tags);
        Assert.Equal(2, loaded.Value.Segments.Count);
        Assert.Equal(Speaker.Me, loaded.Value.Segments[0].Speaker);
        Assert.Equal("Primer mensaje", loaded.Value.Segments[0].Text);
        Assert.Equal(Speaker.Them, loaded.Value.Segments[1].Speaker);
    }

    [Fact]
    public void ListSessions_Filtra_Por_Contenido_De_Transcripcion()
    {
        var idA = _store.SaveSession("Ventas", DateTime.Now, TimeSpan.FromMinutes(1), "", "", [new TranscriptSegment(Speaker.Them, TimeSpan.Zero, TimeSpan.FromSeconds(2), "El presupuesto mensual es 500")]);
        _store.SaveSession("Retrospectiva", DateTime.Now, TimeSpan.FromMinutes(1), "", "", [new TranscriptSegment(Speaker.Me, TimeSpan.Zero, TimeSpan.FromSeconds(2), "Hablemos del sprint")]);

        var results = _store.ListSessions("presupuesto");

        var session = Assert.Single(results);
        Assert.Equal(idA, session.Id);
        Assert.Equal(1, session.SegmentCount);
    }

    [Fact]
    public void UpdateSession_Cambia_Titulo_Tags_Notas()
    {
        var id = _store.SaveSession("Original", DateTime.Now, TimeSpan.FromMinutes(1), "", "", []);
        _store.UpdateSession(id, "Editada", "etiqueta1", "mis notas");

        var loaded = _store.GetSession(id)!.Value.Session;
        Assert.Equal("Editada", loaded.Title);
        Assert.Equal("etiqueta1", loaded.Tags);
        Assert.Equal("mis notas", loaded.Notes);
    }

    [Fact]
    public void DeleteSession_Elimina_Sesion_Y_Segmentos()
    {
        var id = _store.SaveSession("Borrable", DateTime.Now, TimeSpan.FromMinutes(1), "", "", [new TranscriptSegment(Speaker.Me, TimeSpan.Zero, TimeSpan.FromSeconds(1), "texto")]);
        _store.DeleteSession(id);

        Assert.Null(_store.GetSession(id));
        Assert.Empty(_store.ListSessions());
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }
}
