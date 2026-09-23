namespace VoiceRecorder.Core;

/// <summary>Resumen de una sesión guardada (para el historial).</summary>
/// <param name="Id">Identificador en la base de datos.</param>
/// <param name="Title">Título editable.</param>
/// <param name="StartedAt">Fecha/hora local de inicio.</param>
/// <param name="ActiveDuration">Duración activa (sin pausas).</param>
/// <param name="Tags">Etiquetas separadas por coma.</param>
/// <param name="Notes">Notas libres.</param>
/// <param name="SegmentCount">Número de segmentos transcritos.</param>
public sealed record SessionSummary(
    long Id,
    string Title,
    DateTime StartedAt,
    TimeSpan ActiveDuration,
    string Tags,
    string Notes,
    int SegmentCount);
