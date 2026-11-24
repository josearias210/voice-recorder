using VoiceRecorder.Core;

namespace VoiceRecorder.Transcription;

/// <summary>Motor de transcripción de audio mono 16kHz a texto.</summary>
public interface ITranscriptionEngine
{
    /// <summary>
    /// Transcribe un enunciado y devuelve segmentos con timestamps absolutos
    /// (tiempo activo de la sesión).
    /// </summary>
    Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(Utterance utterance, CancellationToken cancellationToken);

    /// <summary>
    /// Olvida el contexto acumulado (texto ya transcrito) antes de empezar
    /// otro archivo o canal. Por defecto no hace nada.
    /// </summary>
    Task ResetContextAsync() => Task.CompletedTask;
}

/// <summary>Motores de transcripción disponibles.</summary>
public enum TranscriptionEngineKind
{
    /// <summary>Whisper (whisper.cpp) en el equipo: gratis y sin enviar nada fuera.</summary>
    Whisper,

    /// <summary>API de OpenAI (gpt-transcribe): envía el audio a OpenAI, de pago.</summary>
    OpenAi,
}
