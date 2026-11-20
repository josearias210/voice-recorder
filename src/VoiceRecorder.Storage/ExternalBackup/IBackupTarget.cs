namespace VoiceRecorder.Storage.Backup;

/// <summary>Archivo presente en el respaldo externo.</summary>
/// <param name="RelativePath">Ruta relativa con '/' (misma que en local).</param>
/// <param name="Size">Tamaño en bytes.</param>
/// <param name="Md5">Hash MD5 del contenido, o null si el destino no lo conoce.</param>
/// <param name="LastModified">Fecha de la última subida.</param>
public sealed record RemoteFile(string RelativePath, long Size, byte[]? Md5, DateTimeOffset? LastModified);

/// <summary>Destino externo donde se replica la carpeta local de sesiones.</summary>
public interface IBackupTarget
{
    /// <summary>Descripción legible (p. ej. "Azure Blob: cuenta/contenedor").</summary>
    string DisplayName { get; }

    /// <summary>Comprueba credenciales y crea el destino si no existe.</summary>
    Task EnsureReadyAsync(CancellationToken cancellationToken);

    /// <summary>Archivos ya respaldados, indexados por ruta relativa ('/').</summary>
    Task<IReadOnlyDictionary<string, RemoteFile>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Sube (o reemplaza) un archivo en la ruta relativa indicada.</summary>
    Task UploadAsync(string relativePath, string localPath, byte[] md5, CancellationToken cancellationToken);

    /// <summary>Descarga un archivo del respaldo a <paramref name="localPath"/> (lo sobrescribe).</summary>
    Task DownloadAsync(string relativePath, string localPath, CancellationToken cancellationToken);

    /// <summary>Lee un archivo pequeño del respaldo como texto UTF-8.</summary>
    Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken);
}
