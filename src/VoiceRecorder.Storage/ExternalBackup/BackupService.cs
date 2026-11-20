using System.Security.Cryptography;

namespace VoiceRecorder.Storage.Backup;

/// <summary>Resultado de subir archivos al respaldo externo.</summary>
/// <param name="Uploaded">Archivos subidos (nuevos o modificados).</param>
/// <param name="Unchanged">Archivos que ya estaban iguales en la nube.</param>
/// <param name="Failed">Archivos que no se pudieron subir.</param>
/// <param name="Error">Primer error encontrado, si lo hubo.</param>
public sealed record BackupResult(int Uploaded, int Unchanged, int Failed, string? Error)
{
    public bool Succeeded => Failed == 0 && Error is null;
}

/// <summary>Resultado de descargar sesiones del respaldo a local.</summary>
/// <param name="Downloaded">Archivos descargados.</param>
/// <param name="Unchanged">Archivos que ya estaban iguales en local.</param>
/// <param name="Skipped">Archivos distintos en local que no se sobrescribieron.</param>
/// <param name="Failed">Archivos que no se pudieron descargar.</param>
/// <param name="Error">Primer error encontrado, si lo hubo.</param>
public sealed record DownloadResult(int Downloaded, int Unchanged, int Skipped, int Failed, string? Error);

/// <summary>Estado de sincronización de un archivo o de una sesión.</summary>
public enum SyncState
{
    /// <summary>Igual en local y en la nube.</summary>
    Synced,

    /// <summary>Está en local y no en la nube (falta subir).</summary>
    LocalOnly,

    /// <summary>Está en la nube y no en local (falta bajar).</summary>
    RemoteOnly,

    /// <summary>Está en ambos lados con contenido distinto.</summary>
    Different,

    /// <summary>Sesión con archivos en varios estados.</summary>
    Mixed,
}

/// <summary>Comparación local/nube de un archivo.</summary>
public sealed record FileSyncStatus(string RelativePath, SyncState State, long LocalBytes, long RemoteBytes);

/// <summary>Comparación local/nube de una carpeta de sesión.</summary>
/// <param name="Folder">Carpeta relativa ('/'), p. ej. "2026/11/20261119-093005-00012".</param>
/// <param name="Title">Título (de su session.json local o de la nube).</param>
/// <param name="StartedAt">Fecha de inicio, si se pudo leer.</param>
/// <param name="Files">Estado de cada archivo.</param>
public sealed record SessionSyncStatus(string Folder, string Title, DateTime? StartedAt, IReadOnlyList<FileSyncStatus> Files)
{
    public int SyncedCount => Files.Count(f => f.State == SyncState.Synced);

    public int LocalOnlyCount => Files.Count(f => f.State == SyncState.LocalOnly);

    public int RemoteOnlyCount => Files.Count(f => f.State == SyncState.RemoteOnly);

    public int DifferentCount => Files.Count(f => f.State == SyncState.Different);

    /// <summary>Estado global: el de todos sus archivos si coinciden, o <see cref="SyncState.Mixed"/>.</summary>
    public SyncState State => Files.Select(f => f.State).Distinct().Count() == 1 ? Files[0].State : SyncState.Mixed;

    /// <summary>Hay algo que subir (archivos solo en local o distintos).</summary>
    public bool HasPendingUpload => LocalOnlyCount > 0 || DifferentCount > 0;

    /// <summary>Hay algo que bajar (archivos solo en la nube o distintos).</summary>
    public bool HasPendingDownload => RemoteOnlyCount > 0 || DifferentCount > 0;

    public long LocalBytes => Files.Sum(f => f.LocalBytes);

    public long RemoteBytes => Files.Sum(f => f.RemoteBytes);
}

/// <summary>Diferencias entre la carpeta local de sesiones y el respaldo.</summary>
public sealed record SyncComparison(IReadOnlyList<SessionSyncStatus> Sessions)
{
    public int TotalFiles => Sessions.Sum(s => s.Files.Count);

    public int SyncedFiles => Sessions.Sum(s => s.SyncedCount);

    public int LocalOnlyFiles => Sessions.Sum(s => s.LocalOnlyCount);

    public int RemoteOnlyFiles => Sessions.Sum(s => s.RemoteOnlyCount);

    public int DifferentFiles => Sessions.Sum(s => s.DifferentCount);

    /// <summary>Porcentaje de archivos iguales en ambos lados (100 si no hay ninguno).</summary>
    public double SyncedPercent => TotalFiles == 0 ? 100 : 100.0 * SyncedFiles / TotalFiles;

    public bool IsFullySynced => SyncedFiles == TotalFiles;

    public int CountSessions(SyncState state) => Sessions.Count(s => s.State == state);
}

/// <summary>
/// Respaldo manual de la carpeta local de sesiones en un <see cref="IBackupTarget"/>
/// con la misma estructura. Nunca sube nada por su cuenta ni borra en la nube:
/// solo compara, sube y baja cuando se le pide.
/// </summary>
public sealed class BackupService : IDisposable
{
    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IBackupTarget? _target;

    public BackupService(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>Destino activo; null desactiva el respaldo.</summary>
    public IBackupTarget? Target
    {
        get => Volatile.Read(ref _target);
        set => Volatile.Write(ref _target, value);
    }

    /// <summary>
    /// Compara local y nube archivo por archivo (MD5) y agrupa por carpeta de
    /// sesión. Las sesiones más recientes primero.
    /// </summary>
    public async Task<SyncComparison> CompareAsync(CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        var target = RequireTarget();
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await target.EnsureReadyAsync(cancellationToken);
            var remote = await target.ListAsync(cancellationToken);
            var local = EnumerateLocalFiles().ToDictionary(f => f.RelativePath, f => f.FullPath, StringComparer.Ordinal);
            var paths = remote.Keys.Union(local.Keys, StringComparer.Ordinal).ToList();

            var files = new List<FileSyncStatus>(paths.Count);
            for (var i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                remote.TryGetValue(path, out var remoteFile);
                local.TryGetValue(path, out var localPath);
                files.Add(await CompareFileAsync(path, localPath, remoteFile, cancellationToken));
                progress?.Report((i + 1) / (double)paths.Count);
            }

            var sessions = new List<SessionSyncStatus>();
            foreach (var group in files.GroupBy(f => FolderOf(f.RelativePath), StringComparer.Ordinal))
            {
                var info = await ReadSessionInfoAsync(target, group.Key, local, remote, cancellationToken);
                sessions.Add(new SessionSyncStatus(
                    group.Key,
                    info?.Title ?? (group.Key.Length == 0 ? "(raíz)" : group.Key[(group.Key.LastIndexOf('/') + 1)..]),
                    info?.StartedAt,
                    group.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList()));
            }

            return new SyncComparison(sessions
                .OrderByDescending(s => s.StartedAt ?? DateTime.MinValue)
                .ThenByDescending(s => s.Folder, StringComparer.Ordinal)
                .ToList());
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Sube lo que falta en la nube y reemplaza los archivos con el mismo nombre
    /// que hayan cambiado; los idénticos no se vuelven a subir. Con
    /// <paramref name="folders"/> se limita a esas carpetas de sesión. Nunca
    /// borra nada en la nube. No lanza: el error va en el resultado.
    /// </summary>
    public async Task<BackupResult> UploadAsync(
        CancellationToken cancellationToken,
        IProgress<double>? progress = null,
        IReadOnlyCollection<string>? folders = null)
    {
        var target = Target;
        if (target is null)
        {
            return new BackupResult(0, 0, 0, "El backup externo no está configurado.");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await UploadCoreAsync(target, folders, cancellationToken, progress);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new BackupResult(0, 0, 0, ex.Message);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Descarga a local los archivos de las carpetas de sesión indicadas, con
    /// la misma estructura. Por defecto solo baja lo que falta; con
    /// <paramref name="overwriteChanged"/> también reemplaza archivos locales
    /// distintos. Nunca borra nada.
    /// </summary>
    public async Task<DownloadResult> DownloadSessionsAsync(
        IReadOnlyCollection<string> folders,
        bool overwriteChanged,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null)
    {
        var target = RequireTarget();
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var wanted = new HashSet<string>(folders.Select(f => f.Trim('/')), StringComparer.Ordinal);
            var remote = await target.ListAsync(cancellationToken);
            var files = remote.Values.Where(f => wanted.Contains(FolderOf(f.RelativePath))).ToList();

            int downloaded = 0, unchanged = 0, skipped = 0, failed = 0;
            string? error = null;
            for (var i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[i];
                try
                {
                    var local = ToLocalPath(file.RelativePath);
                    if (File.Exists(local))
                    {
                        if (SameContent(file, await ComputeMd5Async(local, cancellationToken)))
                        {
                            unchanged++;
                            continue;
                        }

                        if (!overwriteChanged)
                        {
                            skipped++;
                            continue;
                        }
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                    var temp = local + ".download.tmp";
                    await target.DownloadAsync(file.RelativePath, temp, cancellationToken);
                    File.Move(temp, local, overwrite: true);
                    downloaded++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    error ??= $"{file.RelativePath}: {ex.Message}";
                }
                finally
                {
                    progress?.Report((i + 1) / (double)files.Count);
                }
            }

            return new DownloadResult(downloaded, unchanged, skipped, failed, error);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Lista los archivos locales a respaldar como (ruta relativa '/', ruta absoluta).</summary>
    public IReadOnlyList<(string RelativePath, string FullPath)> EnumerateLocalFiles()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_rootDirectory, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path.GetRelativePath(_rootDirectory, f).Replace('\\', '/'), f))
            .OrderBy(f => f.Item1, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<BackupResult> UploadCoreAsync(
        IBackupTarget target,
        IReadOnlyCollection<string>? folders,
        CancellationToken cancellationToken,
        IProgress<double>? progress)
    {
        await target.EnsureReadyAsync(cancellationToken);
        var remote = await target.ListAsync(cancellationToken);
        var wanted = folders is null ? null : new HashSet<string>(folders.Select(f => f.Trim('/')), StringComparer.Ordinal);
        var local = EnumerateLocalFiles()
            .Where(f => wanted is null || wanted.Contains(FolderOf(f.RelativePath)))
            .ToList();

        int uploaded = 0, unchanged = 0, failed = 0;
        string? error = null;
        for (var i = 0; i < local.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (relative, full) = local[i];
            try
            {
                var md5 = await ComputeMd5Async(full, cancellationToken);
                if (remote.TryGetValue(relative, out var remoteFile) && SameContent(remoteFile, md5))
                {
                    unchanged++;
                }
                else
                {
                    await target.UploadAsync(relative, full, md5, cancellationToken);
                    uploaded++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (FileNotFoundException)
            {
                // Se borró mientras subíamos: nada que respaldar.
            }
            catch (DirectoryNotFoundException)
            {
                // La carpeta se borró mientras subíamos: nada que respaldar.
            }
            catch (Exception ex)
            {
                failed++;
                error ??= $"{relative}: {ex.Message}";
            }

            progress?.Report((i + 1) / (double)local.Count);
        }

        return new BackupResult(uploaded, unchanged, failed, error);
    }

    private static async Task<FileSyncStatus> CompareFileAsync(
        string path,
        string? localPath,
        RemoteFile? remote,
        CancellationToken cancellationToken)
    {
        if (localPath is null)
        {
            return new FileSyncStatus(path, SyncState.RemoteOnly, 0, remote!.Size);
        }

        var localBytes = new FileInfo(localPath).Length;
        if (remote is null)
        {
            return new FileSyncStatus(path, SyncState.LocalOnly, localBytes, 0);
        }

        var same = remote.Size == localBytes && SameContent(remote, await ComputeMd5Async(localPath, cancellationToken));
        return new FileSyncStatus(path, same ? SyncState.Synced : SyncState.Different, localBytes, remote.Size);
    }

    /// <summary>Datos de la sesión: del session.json local si existe, si no del de la nube.</summary>
    private static async Task<FileSessionStore.SessionInfo?> ReadSessionInfoAsync(
        IBackupTarget target,
        string folder,
        IReadOnlyDictionary<string, string> local,
        IReadOnlyDictionary<string, RemoteFile> remote,
        CancellationToken cancellationToken)
    {
        var path = folder.Length == 0 ? FileSessionStore.SessionFileName : $"{folder}/{FileSessionStore.SessionFileName}";
        try
        {
            if (local.TryGetValue(path, out var localPath))
            {
                return FileSessionStore.TryParseSessionInfo(await File.ReadAllTextAsync(localPath, cancellationToken));
            }

            if (remote.ContainsKey(path))
            {
                return FileSessionStore.TryParseSessionInfo(await target.ReadTextAsync(path, cancellationToken));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // session.json ilegible: se muestra igualmente con el nombre de la carpeta.
        }

        return null;
    }

    private IBackupTarget RequireTarget() =>
        Target ?? throw new InvalidOperationException("El backup externo no está configurado.");

    private static string FolderOf(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        return slash < 0 ? string.Empty : relativePath[..slash];
    }

    private string ToLocalPath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Join(_rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_rootDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Ruta fuera de la carpeta de sesiones: {relativePath}");
        }

        return full;
    }

    private static bool SameContent(RemoteFile remote, byte[] localMd5) =>
        remote.Md5 is { } md5 && md5.AsSpan().SequenceEqual(localMd5);

    private static async Task<byte[]> ComputeMd5Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await MD5.HashDataAsync(stream, cancellationToken);
    }

    public void Dispose() => _lock.Dispose();
}
