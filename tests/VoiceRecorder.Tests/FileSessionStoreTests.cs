using VoiceRecorder.Core;
using VoiceRecorder.Storage;

namespace VoiceRecorder.Tests;

public sealed class FileSessionStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vr-files-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void SaveSession_Crea_Carpeta_Por_Anio_Mes_Y_Session_Json()
    {
        var store = new FileSessionStore(_root);
        var id = store.SaveSession("Llamada", new DateTime(2026, 11, 19, 9, 30, 5), TimeSpan.FromMinutes(3), "qa", "notas",
            [new TranscriptSegment(Speaker.Me, TimeSpan.Zero, TimeSpan.FromSeconds(2), "hola")]);

        var folder = store.GetSessionFolder(id)!;
        Assert.Equal(Path.Combine(_root, "2026", "11", $"20261119-093005-{id:D5}"), folder);
        Assert.True(File.Exists(Path.Combine(folder, FileSessionStore.SessionFileName)));
    }

    [Fact]
    public void Roundtrip_Y_Persistencia_Entre_Instancias()
    {
        var segments = new List<TranscriptSegment>
        {
            new(Speaker.Them, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8), "Segundo"),
            new(Speaker.Me, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "Primero"),
        };
        var id = new FileSessionStore(_root).SaveSession("Sesión", new DateTime(2026, 9, 23, 9, 0, 0), TimeSpan.FromMinutes(2), "a, b", "n", segments);

        var reopened = new FileSessionStore(_root);
        var (session, loaded) = reopened.GetSession(id)!.Value;
        Assert.Equal("Sesión", session.Title);
        Assert.Equal(new DateTime(2026, 9, 23, 9, 0, 0), session.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(2), session.ActiveDuration);
        Assert.Equal("a, b", session.Tags);
        Assert.Equal(["Primero", "Segundo"], loaded.Select(s => s.Text));
        Assert.Equal(Speaker.Me, loaded[0].Speaker);

        // El contador de Ids continúa tras reabrir.
        var next = reopened.SaveSession("Otra", DateTime.Now, TimeSpan.Zero, "", "", []);
        Assert.True(next > id);
    }

    [Fact]
    public void ListSessions_Busca_En_Titulo_Notas_Y_Transcripcion_Ordenado_Por_Fecha()
    {
        var store = new FileSessionStore(_root);
        var older = store.SaveSession("Ventas", new DateTime(2026, 1, 1), TimeSpan.Zero, "", "",
            [new TranscriptSegment(Speaker.Them, TimeSpan.Zero, TimeSpan.FromSeconds(1), "El Presupuesto es 500")]);
        var newer = store.SaveSession("Retro", new DateTime(2026, 2, 1), TimeSpan.Zero, "", "Importado: clase.mp4", []);

        Assert.Equal([newer, older], store.ListSessions().Select(s => s.Id));
        Assert.Equal(older, Assert.Single(store.ListSessions("presupuesto")).Id);
        Assert.Equal(newer, Assert.Single(store.ListSessions("clase.mp4")).Id);
        Assert.Equal(1, store.ListSessions().Single(s => s.Id == older).SegmentCount);
    }

    [Fact]
    public void Update_Y_Delete()
    {
        var store = new FileSessionStore(_root);
        var id = store.SaveSession("Original", new DateTime(2026, 3, 4), TimeSpan.Zero, "", "", []);
        store.UpdateSession(id, "Editada", "tag", "mis notas");
        var loaded = store.GetSession(id)!.Value.Session;
        Assert.Equal(("Editada", "tag", "mis notas"), (loaded.Title, loaded.Tags, loaded.Notes));

        store.DeleteSession(id);
        Assert.Null(store.GetSession(id));
        Assert.Empty(store.ListSessions());
        Assert.False(Directory.Exists(Path.Combine(_root, "2026")));
    }

    [Fact]
    public void AttachFiles_Mueve_Archivos_A_La_Carpeta_Y_Actualiza_Notas()
    {
        var tempDir = Path.Combine(_root, "..", $"vr-audio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var wav = Path.Combine(tempDir, "grabacion-micro.wav");
        File.WriteAllBytes(wav, [1, 2, 3]);
        try
        {
            var store = new FileSessionStore(_root);
            var changes = 0;
            store.Changed += () => changes++;
            var id = store.SaveSession("Audio", new DateTime(2026, 5, 6), TimeSpan.Zero, "", $"Audio (micro): {wav}", []);

            var moved = Assert.Single(store.AttachFiles(id, [wav]));

            Assert.Equal(Path.Combine(store.GetSessionFolder(id)!, "grabacion-micro.wav"), moved);
            Assert.True(File.Exists(moved));
            Assert.False(File.Exists(wav));
            Assert.Equal($"Audio (micro): {moved}", store.GetSession(id)!.Value.Session.Notes);
            Assert.Equal(2, changes);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SessionJson_Corrupto_Se_Ignora()
    {
        var store = new FileSessionStore(_root);
        var id = store.SaveSession("Buena", new DateTime(2026, 7, 1), TimeSpan.Zero, "", "", []);
        var bad = Path.Combine(_root, "2026", "07", "roto");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, FileSessionStore.SessionFileName), "{ no soy json");

        var reopened = new FileSessionStore(_root);
        Assert.Equal(id, Assert.Single(reopened.ListSessions()).Id);
    }

    [Fact]
    public void Migracion_Desde_Sqlite_Conserva_Ids_Y_Mueve_Audio()
    {
        var dbPath = Path.Combine(_root, "..", $"vr-mig-{Guid.NewGuid():N}.db");
        var wav = Path.Combine(_root, "..", $"vr-mig-{Guid.NewGuid():N}-micro.wav");
        File.WriteAllBytes(wav, [9, 9]);
        long oldId;
        using (var sqlite = new SQLiteSessionStore(dbPath))
        {
            oldId = sqlite.SaveSession("Antigua", new DateTime(2026, 8, 8, 8, 8, 8), TimeSpan.FromSeconds(30), "t", $"Audio (micro): {wav}",
                [new TranscriptSegment(Speaker.Them, TimeSpan.Zero, TimeSpan.FromSeconds(1), "texto viejo")]);
        }

        var store = new FileSessionStore(_root);
        var count = SqliteMigration.MigrateIfNeeded(dbPath, store);

        Assert.Equal(1, count);
        var (session, segments) = store.GetSession(oldId)!.Value;
        Assert.Equal("Antigua", session.Title);
        Assert.Equal("texto viejo", Assert.Single(segments).Text);
        Assert.True(File.Exists(Path.Combine(store.GetSessionFolder(oldId)!, Path.GetFileName(wav))));
        Assert.False(File.Exists(dbPath));
        Assert.True(File.Exists(dbPath + ".migrated"));

        // Segunda vez: no hace nada.
        Assert.Equal(0, SqliteMigration.MigrateIfNeeded(dbPath, store));
        foreach (var f in Directory.EnumerateFiles(Path.GetDirectoryName(dbPath)!, Path.GetFileName(dbPath) + "*"))
        {
            File.Delete(f);
        }
    }
}
