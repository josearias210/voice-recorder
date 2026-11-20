using System.Security.Cryptography;
using VoiceRecorder.Core;
using VoiceRecorder.Storage;
using VoiceRecorder.Storage.Backup;

namespace VoiceRecorder.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vr-backup-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Sync_Replica_La_Estructura_Local_Y_Solo_Sube_Lo_Cambiado()
    {
        var store = new FileSessionStore(_root);
        var id = store.SaveSession("Llamada", new DateTime(2026, 11, 19, 10, 0, 0), TimeSpan.Zero, "", "", []);
        var folder = store.GetSessionFolder(id)!;
        File.WriteAllBytes(Path.Combine(folder, "micro.wav"), [1, 2, 3, 4]);

        var target = new FakeTarget();
        using var backup = new BackupService(_root) { Target = target };

        var first = await backup.SyncAsync(CancellationToken.None);
        Assert.True(first.Succeeded);
        Assert.Equal(2, first.Uploaded);
        var prefix = FileSessionStore.BuildRelativeFolder(id, new DateTime(2026, 11, 19, 10, 0, 0));
        Assert.Contains($"{prefix}/session.json", target.Files.Keys);
        Assert.Contains($"{prefix}/micro.wav", target.Files.Keys);

        var second = await backup.SyncAsync(CancellationToken.None);
        Assert.Equal((0, 2), (second.Uploaded, second.Unchanged));

        store.UpdateSession(id, "Renombrada", "", "");
        var third = await backup.SyncAsync(CancellationToken.None);
        Assert.Equal((1, 1), (third.Uploaded, third.Unchanged));

        // Borrar en local no borra del respaldo.
        store.DeleteSession(id);
        await backup.SyncAsync(CancellationToken.None);
        Assert.Equal(2, target.Files.Count);
    }

    [Fact]
    public async Task Sin_Destino_Devuelve_Error_Sin_Lanzar()
    {
        using var backup = new BackupService(_root);
        var result = await backup.SyncAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Error_Del_Destino_Se_Informa_En_El_Resultado()
    {
        new FileSessionStore(_root).SaveSession("x", DateTime.Now, TimeSpan.Zero, "", "", []);
        using var backup = new BackupService(_root) { Target = new FakeTarget { FailUploads = true } };

        var result = await backup.SyncAsync(CancellationToken.None);

        Assert.Equal(1, result.Failed);
        Assert.Contains("sin red", result.Error);
    }

    [Fact]
    public async Task RequestSync_Agrupa_Peticiones_Y_Notifica()
    {
        var store = new FileSessionStore(_root);
        var target = new FakeTarget();
        using var backup = new BackupService(_root, TimeSpan.FromMilliseconds(50)) { Target = target };
        var done = new TaskCompletionSource<BackupResult>();
        backup.Completed += r => done.TrySetResult(r);
        store.Changed += backup.RequestSync;

        store.SaveSession("a", new DateTime(2026, 1, 1), TimeSpan.Zero, "", "", []);
        store.SaveSession("b", new DateTime(2026, 1, 2), TimeSpan.Zero, "", "", []);

        var result = await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, result.Uploaded);
        Assert.Equal(1, target.ListCalls);
    }

    /// <summary>
    /// Prueba real contra Azure Blob Storage. Solo se ejecuta si existen las
    /// variables VR_TEST_AZURE_ACCOUNT y VR_TEST_AZURE_KEY (p. ej. desde un .env
    /// local que no se sube al repositorio); si no, no hace nada.
    /// </summary>
    [Fact]
    public async Task Azure_Integracion_Sube_Y_Detecta_Archivos_Al_Dia()
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
            File.WriteAllBytes(Path.Combine(store.GetSessionFolder(id)!, "micro.wav"), RandomNumberGenerator.GetBytes(4096));

            using var backup = new BackupService(_root) { Target = target };
            var first = await backup.SyncAsync(CancellationToken.None);
            Assert.True(first.Succeeded, first.Error);
            Assert.Equal(2, first.Uploaded);

            var remote = await target.ListAsync(CancellationToken.None);
            var prefix = FileSessionStore.BuildRelativeFolder(id, new DateTime(2026, 11, 19, 12, 0, 0));
            Assert.Contains($"{prefix}/session.json", remote.Keys);
            Assert.Contains($"{prefix}/micro.wav", remote.Keys);

            var second = await backup.SyncAsync(CancellationToken.None);
            Assert.Equal((0, 2), (second.Uploaded, second.Unchanged));
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

        public int ListCalls { get; private set; }

        public string DisplayName => "fake";

        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, byte[]?>> ListAsync(CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyDictionary<string, byte[]?>>(
                Files.ToDictionary(f => f.Key, f => (byte[]?)MD5.HashData(f.Value)));
        }

        public Task UploadAsync(string relativePath, string localPath, byte[] md5, CancellationToken cancellationToken)
        {
            if (FailUploads)
            {
                throw new IOException("sin red");
            }

            Files[relativePath] = File.ReadAllBytes(localPath);
            return Task.CompletedTask;
        }
    }
}
