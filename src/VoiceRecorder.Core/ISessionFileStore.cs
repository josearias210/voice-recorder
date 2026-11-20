namespace VoiceRecorder.Core;

/// <summary>
/// Almacén de sesiones basado en carpetas: cada sesión tiene su propia carpeta
/// donde, además de la transcripción, se guardan sus archivos (p. ej. los WAV).
/// </summary>
public interface ISessionFileStore : ISessionStore
{
    /// <summary>Carpeta raíz donde viven todas las sesiones.</summary>
    string RootDirectory { get; }

    /// <summary>Se dispara tras crear, modificar o adjuntar archivos a una sesión.</summary>
    event Action? Changed;

    /// <summary>Carpeta de la sesión, o null si no existe.</summary>
    string? GetSessionFolder(long id);

    /// <summary>
    /// Mueve los archivos indicados a la carpeta de la sesión y actualiza en sus
    /// notas las rutas antiguas por las nuevas. Devuelve las rutas finales (en el
    /// mismo orden); si un archivo no se puede mover, conserva su ruta original.
    /// </summary>
    IReadOnlyList<string> AttachFiles(long id, IReadOnlyList<string> sourcePaths);
}
