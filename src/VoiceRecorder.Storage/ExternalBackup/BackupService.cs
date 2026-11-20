using System.Security.Cryptography;

namespace VoiceRecorder.Storage.Backup;

/// <summary>Resultado de una sincronización con el respaldo externo.</summary>
/// <param name="Uploaded">Archivos subidos (nuevos o modificados).</param>
/// <param name="Unchanged">Archivos que ya estaban respaldados.</param>
/// <param name="Failed">Archivos que no se pudieron subir.</param>
/// <param name="Error">Primer error encontrado, si lo hubo.</param>
public sealed record BackupResult(int Uploaded, int Unchanged, int Failed, string? Error)
{
    public bool Succeeded => Failed == 0 && Error is null;
}

/// <summary>
/// Replica la carpeta local de sesiones en un <see cref="IBackupTarget"/>
/// con la misma estructura. Solo añade o reemplaza: borrar una sesión en
/// local no la borra del respaldo.
/// </summary>
public sealed class BackupService : IDisposable
{
    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly TimeSpan _debounce;
    private CancellationTokenSource? _pending;
    private IBackupTarget? _target;

    public BackupService(string rootDirectory, TimeSpan? debounce = null)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _debounce = debounce ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>Se dispara al terminar una sincronización (en un hilo de fondo).</summary>
    public event Action<BackupResult>? Completed;

    /// <summary>Destino activo; null desactiva el respaldo.</summary>
    public IBackupTarget? Target
    {
        get => Volatile.Read(ref _target);
        set => Volatile.Write(ref _target, value);
    }

    public BackupResult? LastResult { get; private set; }

    public DateTime? LastSyncAt { get; private set; }

    /// <summary>
    /// Programa una sincronización en segundo plano. Las peticiones seguidas se
    /// agrupan (debounce) para no subir varias veces lo mismo.
    /// </summary>
    public void RequestSync()
    {
        if (Target is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _pending, cts)?.Cancel();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounce, cts.Token);
                await SyncAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Sustituida por una petición más reciente.
            }
        });
    }

    /// <summary>Sube los archivos nuevos o modificados. Nunca lanza: el error va en el resultado.</summary>
    public async Task<BackupResult> SyncAsync(CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        var target = Target;
        if (target is null)
        {
            return new BackupResult(0, 0, 0, "El respaldo externo no está configurado.");
        }

        await _syncLock.WaitAsync(cancellationToken);
        BackupResult result;
        try
        {
            result = await SyncCoreAsync(target, cancellationToken, progress);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = new BackupResult(0, 0, 0, ex.Message);
        }
        finally
        {
            _syncLock.Release();
        }

        LastResult = result;
        LastSyncAt = DateTime.Now;
        Completed?.Invoke(result);
        return result;
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

    private async Task<BackupResult> SyncCoreAsync(IBackupTarget target, CancellationToken cancellationToken, IProgress<double>? progress)
    {
        await target.EnsureReadyAsync(cancellationToken);
        var remote = await target.ListAsync(cancellationToken);
        var local = EnumerateLocalFiles();

        int uploaded = 0, unchanged = 0, failed = 0;
        string? error = null;
        for (var i = 0; i < local.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (relative, full) = local[i];
            try
            {
                var md5 = await ComputeMd5Async(full, cancellationToken);
                if (remote.TryGetValue(relative, out var remoteMd5) && remoteMd5 is not null && remoteMd5.AsSpan().SequenceEqual(md5))
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
                // Se borró mientras sincronizábamos: nada que respaldar.
            }
            catch (DirectoryNotFoundException)
            {
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

    private static async Task<byte[]> ComputeMd5Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await MD5.HashDataAsync(stream, cancellationToken);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _pending, null)?.Cancel();
        _syncLock.Dispose();
    }
}
