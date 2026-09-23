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
}
