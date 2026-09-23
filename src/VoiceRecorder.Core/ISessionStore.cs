namespace VoiceRecorder.Core;

/// <summary>Almacén persistente de sesiones y sus transcripciones.</summary>
public interface ISessionStore
{
    /// <summary>Guarda una sesión con sus segmentos y devuelve su Id.</summary>
    long SaveSession(
        string title,
        DateTime startedAt,
        TimeSpan activeDuration,
        string tags,
        string notes,
        IReadOnlyList<TranscriptSegment> segments);

    /// <summary>Lista sesiones, opcionalmente filtrando por texto (título/tags/notas/transcripción).</summary>
    IReadOnlyList<SessionSummary> ListSessions(string? searchText = null);

    /// <summary>Obtiene una sesión con sus segmentos ordenados por tiempo, o null si no existe.</summary>
    (SessionSummary Session, IReadOnlyList<TranscriptSegment> Segments)? GetSession(long id);

    /// <summary>Actualiza metadatos de la sesión.</summary>
    void UpdateSession(long id, string title, string tags, string notes);

    /// <summary>Elimina una sesión y sus segmentos.</summary>
    void DeleteSession(long id);
}
