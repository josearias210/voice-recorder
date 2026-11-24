namespace VoiceRecorder.Core;

/// <summary>Una transcripción ejecutada: con qué motor, cuánto tardó, cuánto audio procesó y cuánto costó.</summary>
/// <param name="StartedAt">Fecha y hora local en que empezó la transcripción.</param>
/// <param name="Engine">Motor usado ("Whisper" u "OpenAI").</param>
/// <param name="Model">Modelo usado (p. ej. "Large v3 Turbo" o "gpt-transcribe").</param>
/// <param name="Elapsed">Tiempo que tardó la transcripción.</param>
/// <param name="AudioSeconds">
/// Audio procesado, en segundos: con Whisper, la duración de los archivos;
/// con OpenAI, el audio enviado (solo los tramos con voz), que es lo que se factura.
/// </param>
/// <param name="CostUsd">Coste estimado en USD (0 con Whisper local).</param>
/// <param name="SessionId">Sesión transcrita, si la hay.</param>
/// <param name="SessionTitle">Título de la sesión en el momento de transcribir.</param>
/// <param name="Completed">false si falló o se canceló a medias (con OpenAI, lo enviado se cobra igual).</param>
public sealed record TranscriptionRun(
    DateTime StartedAt,
    string Engine,
    string Model,
    TimeSpan Elapsed,
    double AudioSeconds,
    double CostUsd,
    long? SessionId = null,
    string? SessionTitle = null,
    bool Completed = true)
{
    /// <summary>Indica si el motor es de pago (cualquier coste mayor que cero).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPaid => CostUsd > 0;
}
