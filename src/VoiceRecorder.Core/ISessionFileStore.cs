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

    /// <summary>
    /// Vuelve a explorar la carpeta raíz para registrar sesiones añadidas desde
    /// fuera (p. ej. descargadas del respaldo). Devuelve cuántas son nuevas.
    /// </summary>
    int Refresh();

    /// <summary>Carpeta de la sesión, o null si no existe.</summary>
    string? GetSessionFolder(long id);

    /// <summary>
    /// Mueve los archivos indicados a la carpeta de la sesión y actualiza en sus
    /// notas las rutas antiguas por las nuevas. Devuelve las rutas finales (en el
    /// mismo orden); si un archivo no se puede mover, conserva su ruta original.
    /// </summary>
    IReadOnlyList<string> AttachFiles(long id, IReadOnlyList<string> sourcePaths);

    /// <summary>
    /// Sustituye la transcripción de la sesión (al transcribir más tarde o
    /// regenerarla) y registra con qué motor se hizo (<paramref name="run"/>).
    /// Devuelve false si la sesión no existe.
    /// </summary>
    bool ReplaceSegments(long id, IReadOnlyList<TranscriptSegment> segments, TranscriptionRun? run = null);

    /// <summary>Saves (or, with null, removes) the session's AI summary. Returns false if the session does not exist.</summary>
    bool SetCallSummary(long id, CallSummary? summary);

    /// <summary>Saves (or, with null, removes) the session's communication review. Returns false if the session does not exist.</summary>
    bool SetReview(long id, CallSummary? review);

    /// <summary>Records (or, with null, forgets) the Notion page of the session. Returns false if the session does not exist.</summary>
    bool SetNotionExport(long id, NotionExport? export);
}
