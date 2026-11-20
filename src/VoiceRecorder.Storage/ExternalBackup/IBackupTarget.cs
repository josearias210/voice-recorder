namespace VoiceRecorder.Storage.Backup;

/// <summary>Destino externo donde se replica la carpeta local de sesiones.</summary>
public interface IBackupTarget
{
    /// <summary>Descripción legible (p. ej. "Azure Blob: cuenta/contenedor").</summary>
    string DisplayName { get; }

    /// <summary>Comprueba credenciales y crea el destino si no existe.</summary>
    Task EnsureReadyAsync(CancellationToken cancellationToken);

    /// <summary>Archivos ya respaldados: ruta relativa ('/') → MD5 (o null si se desconoce).</summary>
    Task<IReadOnlyDictionary<string, byte[]?>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Sube (o reemplaza) un archivo en la ruta relativa indicada.</summary>
    Task UploadAsync(string relativePath, string localPath, byte[] md5, CancellationToken cancellationToken);
}
