using System.Security.Cryptography;
using VoiceRecorder.Core;
using VoiceRecorder.Storage;
using VoiceRecorder.Storage.Backup;

namespace VoiceRecorder.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _root = Path.Join(Path.GetTempPath(), $"vr-backup-{Guid.NewGuid():N}");

    public void Dispose()
    {
        foreach (var dir in new[] { _root, _root + "-b", _root + "-otro" })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Guardar_Sesiones_No_Sube_Nada_Por_Su_Cuenta()
    {
        var target = new FakeTarget();
        using var backup = new BackupService(_root) { Target = target };
        var store = new FileSessionStore(_root);
        var id = store.SaveSession("a", new DateTime(2026, 1, 1), TimeSpan.Zero, "", "", []);
        store.UpdateSession(id, "b", "", "");

        await Task.Delay(300);
        Assert.Equal(0, target.UploadCalls);
        Assert.Empty(target.Files);
    }

    [Fact]
    public async Task Subir_Replica_La_Estructura_Local_Y_Solo_Sube_Lo_Cambiado()
    {
        var store = new FileSessionStore(_root);
        var id = store.SaveSession("Llamada", new DateTime(2026, 11, 19, 10, 0, 0), TimeSpan.Zero, "", "", []);
        File.WriteAllBytes(Path.Join(store.GetSessionFolder(id)!, "micro.wav"), [1, 2, 3, 4]);

        var target = new FakeTarget();
        using var backup = new BackupService(_root) { Target = target };

        var first = await backup.UploadAsync(CancellationToken.None);
        Assert.True(first.Succeeded);
        Assert.Equal(2, first.Uploaded);
        var prefix = FileSessionStore.BuildRelativeFolder(id, new DateTime(2026, 11, 19, 10, 0, 0));
        Assert.Contains($"{prefix}/session.json", target.Files.Keys);
        Assert.Contains($"{prefix}/micro.wav", target.Files.Keys);

        var second = await backup.UploadAsync(CancellationToken.None);
        Assert.Equal((0, 2), (second.Uploaded, second.Unchanged));

        store.UpdateSession(id, "Renombrada", "", "");
        var third = await backup.UploadAsync(CancellationToken.None);
        Assert.Equal((1, 1), (third.Uploaded, third.Unchanged));
        Assert.Equal(3, target.UploadCalls);

        // Borrar en local no borra en la nube.
        store.DeleteSession(id);
        await backup.UploadAsync(CancellationToken.None);
        Assert.Equal(2, target.Files.Count);
    }

    [Fact]
    public async Task Subir_Marcadas_Solo_Sube_Esas_Carpetas()
    {
        var store = new FileSessionStore(_root);
        var a = store.SaveSession("a", new DateTime(2026, 2, 1), TimeSpan.Zero, "", "", []);
        store.SaveSession("b", new DateTime(2026, 2, 2), TimeSpan.Zero, "", "", []);
        var folderA = FileSessionStore.BuildRelativeFolder(a, new DateTime(2026, 2, 1));
        var target = new FakeTarget();
        using var backup = new BackupService(_root) { Target = target };

        var result = await backup.UploadAsync(CancellationToken.None, folders: [folderA]);

        Assert.Equal(1, result.Uploaded);
        Assert.Equal($"{folderA}/session.json", Assert.Single(target.Files.Keys));
    }

    [Fact]
    public async Task Comparar_Distingue_Falta_Subir_Falta_Bajar_Distinto_Y_Sincronizado()
    {
        var store = new FileSessionStore(_root);
        var synced = store.SaveSession("Sincronizada", new DateTime(2026, 3, 1), TimeSpan.Zero, "", "", []);
        var changed = store.SaveSession("Cambiada", new DateTime(2026, 3, 2), TimeSpan.Zero, "", "", []);
        var target = new FakeTarget();
        using var backup = new BackupService(_root) { Target = target };
        await backup.UploadAsync(CancellationToken.None);

        store.SaveSession("Nueva local", new DateTime(2026, 3, 3), TimeSpan.Zero, "", "", []);
        store.UpdateSession(changed, "Cambiada en local", "", "");
        File.WriteAllBytes(Path.Join(store.GetSessionFolder(synced)!, "micro.wav"), [5, 5]);
        var remoteOnlyFolder = "2026/03/20260304-000000-00099";
        target.Files[$"{remoteOnlyFolder}/session.json"] = System.Text.Encoding.UTF8.GetBytes(
            """{"version":1,"id":99,"title":"De otro equipo","startedAt":"2026-03-04T00:00:00","activeDurationSeconds":0,"tags":"","notes":"","segments":[]}""");

        var comparison = await backup.CompareAsync(CancellationToken.None);

        var byTitle = comparison.Sessions.ToDictionary(s => s.Title);
        Assert.Equal(SyncState.LocalOnly, byTitle["Nueva local"].State);
        Assert.Equal(SyncState.RemoteOnly, byTitle["De otro equipo"].State);
        Assert.Equal(SyncState.Different, byTitle["Cambiada en local"].State);
        Assert.Equal(SyncState.Mixed, byTitle["Sincronizada"].State);
        Assert.Equal((1, 1), (byTitle["Sincronizada"].SyncedCount, byTitle["Sincronizada"].LocalOnlyCount));

        Assert.Equal(5, comparison.TotalFiles);
        Assert.Equal(1, comparison.SyncedFiles);
        Assert.Equal(2, comparison.LocalOnlyFiles);
        Assert.Equal(1, comparison.RemoteOnlyFiles);
        Assert.Equal(1, comparison.DifferentFiles);
        Assert.Equal(20, comparison.SyncedPercent);
        Assert.False(comparison.IsFullySynced);
        Assert.Equal(["De otro equipo", "Nueva local", "Cambiada en local", "Sincronizada"], comparison.Sessions.Select(s => s.Title));

        // Subir todo y bajar lo remoto deja todo al 100 %.
        await backup.UploadAsync(CancellationToken.None);
        await backup.DownloadSessionsAsync([remoteOnlyFolder], false, CancellationToken.None);
        var after = await backup.CompareAsync(CancellationToken.None);
        Assert.True(after.IsFullySynced);
        Assert.Equal(100, after.SyncedPercent);
        Assert.All(after.Sessions, s => Assert.Equal(SyncState.Synced, s.State));
    }

    [Fact]
    public async Task Sin_Destino_Subir_Devuelve_Error_Sin_Lanzar()
    {
        using var backup = new BackupService(_root);
        var result = await backup.UploadAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Error_Del_Destino_Se_Informa_En_El_Resultado()
    {
        new FileSessionStore(_root).SaveSession("x", DateTime.Now, TimeSpan.Zero, "", "", []);
        using var backup = new BackupService(_root) { Target = new FakeTarget { FailUploads = true } };

        var result = await backup.UploadAsync(CancellationToken.None);

        Assert.Equal(1, result.Failed);
        Assert.Contains("sin red", result.Error);
    }

    [Fact]
    public async Task Subir_Reemplaza_Archivo_Cambiado_Con_El_Mismo_Nombre_Y_No_Duplica()
    {
        var store = new FileSessionStore(_root);
        var id = store.SaveSession("x", new DateTime(2026, 4, 4), TimeSpan.Zero, "", "", []);
        var wav = Path.Join(store.GetSessionFolder(id)!, "micro.wav");
        File.WriteAllBytes(wav, [1, 1]);
        var target = new FakeTarget();
        using var backup = new BackupService(_root) { Target = target };
        await backup.UploadAsync(CancellationToken.None);

        File.WriteAllBytes(wav, [2, 2, 2]);
        var result = await backup.UploadAsync(CancellationToken.None);

        Assert.Equal((1, 1), (result.Uploaded, result.Unchanged));
        Assert.Equal(2, target.Files.Count);
        Assert.Equal(new byte[] { 2, 2, 2 }, target.Files.Single(f => f.Key.EndsWith("micro.wav")).Value);
        Assert.Equal(3, target.UploadCalls);
    }

    [Fact]
    public async Task Bajar_En_Otro_Equipo_Baja_Lo_Que_Falta_Y_Respeta_Cambios_Locales()
    {
        // Equipo A sube dos sesiones.
        var storeA = new FileSessionStore(_root);
        var id1 = storeA.SaveSession("Primera", new DateTime(2026, 6, 1, 10, 0, 0), TimeSpan.Zero, "", "",
            [new TranscriptSegment(Speaker.Me, TimeSpan.Zero, TimeSpan.FromSeconds(1), "uno")]);
        storeA.SaveSession("Segunda", new DateTime(2026, 6, 2, 10, 0, 0), TimeSpan.Zero, "", "", []);
        File.WriteAllBytes(Path.Join(storeA.GetSessionFolder(id1)!, "micro.wav"), [7, 7, 7]);
        var target = new FakeTarget();
        using (var backupA = new BackupService(_root) { Target = target })
        {
            await backupA.UploadAsync(CancellationToken.None);
        }

        // Equipo B ya tiene su propia sesión con Id 1.
        var rootB = _root + "-b";
        var storeB = new FileSessionStore(rootB);
        var localId = storeB.SaveSession("Local B", new DateTime(2026, 6, 3), TimeSpan.Zero, "", "", []);
        using var backupB = new BackupService(rootB) { Target = target };

        var comparison = await backupB.CompareAsync(CancellationToken.None);
        Assert.Equal(["Local B", "Segunda", "Primera"], comparison.Sessions.Select(r => r.Title));
        Assert.Equal(SyncState.LocalOnly, comparison.Sessions[0].State);
        Assert.Equal(SyncState.RemoteOnly, comparison.Sessions[2].State);

        var first = comparison.Sessions.Single(r => r.Title == "Primera");
        var result = await backupB.DownloadSessionsAsync([first.Folder], false, CancellationToken.None);
        Assert.Equal(2, result.Downloaded);
        Assert.Equal(1, storeB.Refresh());

        // La sesión descargada aparece con un Id distinto al de la local.
        var titles = storeB.ListSessions().ToDictionary(s => s.Title, s => s.Id);
        Assert.Equal(localId, titles["Local B"]);
        Assert.NotEqual(localId, titles["Primera"]);
        Assert.Equal("uno", Assert.Single(storeB.GetSession(titles["Primera"])!.Value.Segments).Text);

        // Ya está sincronizada; un cambio local no se pisa salvo que se pida.
        Assert.Equal(SyncState.Synced, (await backupB.CompareAsync(CancellationToken.None)).Sessions.Single(r => r.Title == "Primera").State);
        var localWav = Path.Join(storeB.GetSessionFolder(titles["Primera"])!, "micro.wav");
        File.WriteAllBytes(localWav, [9]);
        var again = await backupB.DownloadSessionsAsync([first.Folder], false, CancellationToken.None);
        Assert.Equal((0, 1, 1), (again.Downloaded, again.Unchanged, again.Skipped));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(localWav));

        var forced = await backupB.DownloadSessionsAsync([first.Folder], true, CancellationToken.None);
        Assert.Equal(1, forced.Downloaded);
        Assert.Equal(new byte[] { 7, 7, 7 }, File.ReadAllBytes(localWav));

        // Nada se borra en la nube.
        Assert.Equal(3, target.Files.Count);
    }

    /// <summary>
    /// Prueba real contra Azure Blob Storage. Solo se ejecuta si existen las
    /// variables VR_TEST_AZURE_ACCOUNT y VR_TEST_AZURE_KEY (p. ej. desde un .env
    /// local que no se sube al repositorio); si no, no hace nada.
    /// </summary>
    [Fact]
    public async Task Azure_Integracion_Compara_Sube_Y_Baja()
    {
        var account = Environment.GetEnvironmentVariable("VR_TEST_AZURE_ACCOUNT");
        var key = Environment.GetEnvironmentVariable("VR_TEST_AZURE_KEY");
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var container = Environment.GetEnvironmentVariable("VR_TEST_AZURE_CONTAINER");
        var target = new AzureBlobBackupTarget(account, key, container, $"pruebas/{Guid.NewGuid():N}");
        try
        {
            var store = new FileSessionStore(_root);
            var id = store.SaveSession("Integración", new DateTime(2026, 11, 19, 12, 0, 0), TimeSpan.FromSeconds(5), "", "",
                [new TranscriptSegment(Speaker.Me, TimeSpan.Zero, TimeSpan.FromSeconds(1), "hola Azure")]);
            File.WriteAllBytes(Path.Join(store.GetSessionFolder(id)!, "micro.wav"), RandomNumberGenerator.GetBytes(4096));

            using var backup = new BackupService(_root) { Target = target };
            var before = await backup.CompareAsync(CancellationToken.None);
            Assert.Equal((2, 0), (before.LocalOnlyFiles, before.SyncedFiles));

            var first = await backup.UploadAsync(CancellationToken.None);
            Assert.True(first.Succeeded, first.Error);
            Assert.Equal(2, first.Uploaded);

            var after = await backup.CompareAsync(CancellationToken.None);
            Assert.True(after.IsFullySynced);
            var second = await backup.UploadAsync(CancellationToken.None);
            Assert.Equal((0, 2), (second.Uploaded, second.Unchanged));

            // "Otro equipo": ve la sesión como solo en la nube y la baja.
            using var other = new BackupService(_root + "-otro") { Target = target };
            var remote = Assert.Single((await other.CompareAsync(CancellationToken.None)).Sessions);
            Assert.Equal(("Integración", SyncState.RemoteOnly), (remote.Title, remote.State));
            var download = await other.DownloadSessionsAsync([remote.Folder], false, CancellationToken.None);
            Assert.Equal(2, download.Downloaded);
            var otherStore = new FileSessionStore(_root + "-otro");
            Assert.Equal("hola Azure", Assert.Single(otherStore.GetSession(Assert.Single(otherStore.ListSessions()).Id)!.Value.Segments).Text);
            Assert.True((await other.CompareAsync(CancellationToken.None)).IsFullySynced);
        }
        finally
        {
            await target.DeleteAllAsync(CancellationToken.None);
        }
    }

    private sealed class FakeTarget : IBackupTarget
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public bool FailUploads { get; init; }

        public int UploadCalls { get; private set; }

        public string DisplayName => "fake";

        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, RemoteFile>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, RemoteFile>>(
                Files.ToDictionary(f => f.Key, f => new RemoteFile(f.Key, f.Value.Length, MD5.HashData(f.Value), DateTimeOffset.UtcNow)));

        public Task UploadAsync(string relativePath, string localPath, byte[] md5, CancellationToken cancellationToken)
        {
            if (FailUploads)
            {
                throw new IOException("sin red");
            }

            UploadCalls++;
            Files[relativePath] = File.ReadAllBytes(localPath);
            return Task.CompletedTask;
        }

        public Task DownloadAsync(string relativePath, string localPath, CancellationToken cancellationToken)
        {
            File.WriteAllBytes(localPath, Files[relativePath]);
            return Task.CompletedTask;
        }

        public Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken) =>
            Task.FromResult(System.Text.Encoding.UTF8.GetString(Files[relativePath]));
    }
}
