using VoiceRecorder.Core;
using VoiceRecorder.Storage;

namespace VoiceRecorder.Tests;

public sealed class FileSessionStoreTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), $"vr-files-{Guid.NewGuid():N}");

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
        Assert.Equal(Path.Join(_root, "2026", "11", $"20261119-093005-{id:D5}"), folder);
        Assert.True(File.Exists(Path.Join(folder, FileSessionStore.SessionFileName)));
    }

    [Fact]
    public void El_Resumen_Y_La_Pagina_De_Notion_Se_Guardan_Y_Sobreviven_A_Retranscribir()
    {
        var store = new FileSessionStore(_root);
        var id = store.SaveSession("Llamada", new DateTime(2026, 11, 24, 10, 0, 0), TimeSpan.FromMinutes(5), "qa", "notas", []);
        var summary = new CallSummary("## Resumen\n- Punto", new DateTime(2026, 11, 24, 10, 6, 0), "gpt-5-mini", 0.0021);
        var export = new NotionExport("page-1", "https://www.notion.so/page-1", new DateTime(2026, 11, 24, 10, 7, 0));

        Assert.True(store.SetCallSummary(id, summary));
        Assert.True(store.SetNotionExport(id, export));
        store.ReplaceSegments(id, [new TranscriptSegment(Speaker.Me, TimeSpan.Zero, TimeSpan.FromSeconds(1), "hola")]);
        store.UpdateSession(id, "Llamada editada", "qa", "notas");

        var reopened = new FileSessionStore(_root);
        var (session, _) = reopened.GetSession(id)!.Value;
        Assert.Equal(summary, session.CallSummary);
        Assert.Equal(export, session.Notion);
        Assert.Equal(summary, Assert.Single(reopened.ListSessions()).CallSummary);

        var review = new CallSummary("## Fallas\n- Interrumpí", new DateTime(2026, 11, 24, 10, 8, 0), "gpt-5-mini", 0.003);
        Assert.True(store.SetReview(id, review));
        Assert.Equal(review, new FileSessionStore(_root).GetSession(id)!.Value.Session.Review);

        var info = FileSessionStore.TryParseSessionInfo(File.ReadAllText(Path.Join(store.GetSessionFolder(id)!, FileSessionStore.SessionFileName)))!;
        Assert.Equal(1, info.SegmentCount);
        Assert.True(info.HasSummary);
        Assert.True(info.HasReview);
        Assert.True(info.InNotion);

        Assert.True(store.SetCallSummary(id, null));
        Assert.Null(new FileSessionStore(_root).GetSession(id)!.Value.Session.CallSummary);
        Assert.False(store.SetNotionExport(999, export));
    }

    [Fact]
    public void ReplaceSegments_Sustituye_La_Transcripcion_Y_Conserva_Los_Metadatos()
    {
        var store = new FileSessionStore(_root);
        var id = store.SaveSession("Llamada", new DateTime(2026, 11, 23, 10, 0, 0), TimeSpan.FromMinutes(5), "qa", "notas", []);

        var replaced = store.ReplaceSegments(id,
        [
            new TranscriptSegment(Speaker.Them, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), "después"),
            new TranscriptSegment(Speaker.Me, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "antes"),
        ]);

        Assert.True(replaced);
        var (session, segments) = new FileSessionStore(_root).GetSession(id)!.Value;
        Assert.Equal("Llamada", session.Title);
        Assert.Equal("notas", session.Notes);
        Assert.Equal(["antes", "después"], segments.Select(s => s.Text));
        Assert.False(store.ReplaceSegments(id + 100, []));
    }

    [Fact]
    public void ReplaceSegments_Guarda_La_Ultima_Transcripcion_En_El_Historial()
    {
        var store = new FileSessionStore(_root);
        var id = store.SaveSession("Llamada", new DateTime(2026, 11, 23, 10, 0, 0), TimeSpan.FromMinutes(5), "", "", []);
        var run = new TranscriptionRun(new DateTime(2026, 11, 23, 10, 6, 0), "OpenAI", "gpt-transcribe", TimeSpan.FromSeconds(95), 240, 0.018, id, "Llamada");

        store.ReplaceSegments(id, [new TranscriptSegment(Speaker.Me, TimeSpan.Zero, TimeSpan.FromSeconds(1), "hola")], run);
        store.ReplaceSegments(id, [new TranscriptSegment(Speaker.Me, TimeSpan.Zero, TimeSpan.FromSeconds(1), "hola")]);

        var summary = Assert.Single(new FileSessionStore(_root).ListSessions());
        Assert.Equal(run, summary.LastTranscription); // Sin run nuevo se conserva el anterior.
        Assert.Null(new FileSessionStore(_root).GetSession(id + 1));
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
        Assert.False(Directory.Exists(Path.Join(_root, "2026")));
    }

    [Fact]
    public void AttachFiles_Mueve_Archivos_A_La_Carpeta_Y_Actualiza_Notas()
    {
        var tempDir = Path.Join(_root, "..", $"vr-audio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var wav = Path.Join(tempDir, "grabacion-micro.wav");
        File.WriteAllBytes(wav, [1, 2, 3]);
        try
        {
            var store = new FileSessionStore(_root);
            var changes = 0;
            store.Changed += () => changes++;
            var id = store.SaveSession("Audio", new DateTime(2026, 5, 6), TimeSpan.Zero, "", $"Audio (micro): {wav}", []);

            var moved = Assert.Single(store.AttachFiles(id, [wav]));

            Assert.Equal(Path.Join(store.GetSessionFolder(id)!, "grabacion-micro.wav"), moved);
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
        var bad = Path.Join(_root, "2026", "07", "roto");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Join(bad, FileSessionStore.SessionFileName), "{ no soy json");

        var reopened = new FileSessionStore(_root);
        Assert.Equal(id, Assert.Single(reopened.ListSessions()).Id);
    }

    [Fact]
    public void Migracion_Desde_Sqlite_Conserva_Ids_Y_Mueve_Audio()
    {
        var dbPath = Path.Join(_root, "..", $"vr-mig-{Guid.NewGuid():N}.db");
        var wav = Path.Join(_root, "..", $"vr-mig-{Guid.NewGuid():N}-micro.wav");
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
        Assert.True(File.Exists(Path.Join(store.GetSessionFolder(oldId)!, Path.GetFileName(wav))));
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
