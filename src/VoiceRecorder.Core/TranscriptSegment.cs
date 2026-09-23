namespace VoiceRecorder.Core;

/// <summary>Segmento de transcripción con hablante y timestamps de tiempo activo.</summary>
/// <param name="Speaker">Hablante atribuido.</param>
/// <param name="Start">Inicio del segmento (tiempo activo de sesión).</param>
/// <param name="End">Fin del segmento (tiempo activo de sesión).</param>
/// <param name="Text">Texto transcrito.</param>
public sealed record TranscriptSegment(Speaker Speaker, TimeSpan Start, TimeSpan End, string Text);
