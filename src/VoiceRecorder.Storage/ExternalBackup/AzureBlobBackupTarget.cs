using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace VoiceRecorder.Storage.Backup;

/// <summary>
/// Respaldo en un contenedor de Azure Blob Storage. Cada archivo local se sube
/// con la misma ruta relativa que tiene dentro de la carpeta de sesiones.
/// </summary>
public sealed class AzureBlobBackupTarget : IBackupTarget
{
    /// <summary>Contenedor por defecto si no se configura otro.</summary>
    public const string DefaultContainer = "voice-recorder";

    private readonly BlobContainerClient _container;
    private readonly string _prefix;

    /// <summary>Crea el destino de backup sobre un contenedor de Azure Blob Storage.</summary>
    /// <param name="accountName">Nombre de la cuenta de almacenamiento.</param>
    /// <param name="accountKey">Clave de acceso de la cuenta, o una cadena de conexión completa.</param>
    /// <param name="containerName">Contenedor (se crea si no existe).</param>
    /// <param name="prefix">Prefijo opcional dentro del contenedor (sin '/' final).</param>
    public AzureBlobBackupTarget(string accountName, string accountKey, string? containerName = null, string? prefix = null)
    {
        if (string.IsNullOrWhiteSpace(accountKey))
        {
            throw new ArgumentException("Falta la clave de la cuenta de almacenamiento.", nameof(accountKey));
        }

        var container = string.IsNullOrWhiteSpace(containerName) ? DefaultContainer : containerName.Trim().ToLowerInvariant();
        var key = accountKey.Trim();
        if (key.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase)
            || key.Contains("SharedAccessSignature=", StringComparison.OrdinalIgnoreCase))
        {
            _container = new BlobContainerClient(key, container);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(accountName))
            {
                throw new ArgumentException("Falta el nombre de la cuenta de almacenamiento.", nameof(accountName));
            }

            var account = accountName.Trim().ToLowerInvariant();
            _container = new BlobContainerClient(
                new Uri($"https://{account}.blob.core.windows.net/{container}"),
                new StorageSharedKeyCredential(account, key));
        }

        _prefix = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix.Trim('/') + "/";
    }

    public string DisplayName => $"Azure Blob: {_container.AccountName}/{_container.Name}";

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, RemoteFile>> ListAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, RemoteFile>(StringComparer.Ordinal);
        await foreach (var blob in _container.GetBlobsAsync(
                           new GetBlobsOptions { Prefix = _prefix.Length > 0 ? _prefix : null },
                           cancellationToken))
        {
            var relative = blob.Name[_prefix.Length..];
            result[relative] = new RemoteFile(
                relative,
                blob.Properties.ContentLength ?? 0,
                blob.Properties.ContentHash,
                blob.Properties.LastModified);
        }

        return result;
    }

    public async Task DownloadAsync(string relativePath, string localPath, CancellationToken cancellationToken)
    {
        await _container.GetBlobClient(_prefix + relativePath).DownloadToAsync(localPath, cancellationToken);
    }

    public async Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken)
    {
        var content = await _container.GetBlobClient(_prefix + relativePath).DownloadContentAsync(cancellationToken);
        return content.Value.Content.ToString();
    }

    public async Task UploadAsync(string relativePath, string localPath, byte[] md5, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(_prefix + relativePath);
        await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        await blob.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentHash = md5,
                    ContentType = ContentTypeFor(localPath),
                },
            },
            cancellationToken);
    }

    /// <summary>Elimina todos los blobs bajo el prefijo (usado por las pruebas).</summary>
    public async Task DeleteAllAsync(CancellationToken cancellationToken)
    {
        await foreach (var blob in _container.GetBlobsAsync(
                           new GetBlobsOptions { Prefix = _prefix.Length > 0 ? _prefix : null },
                           cancellationToken))
        {
            try
            {
                await _container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException)
            {
                // Limpieza "best effort".
            }
        }
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        ".txt" or ".md" or ".srt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };
}
