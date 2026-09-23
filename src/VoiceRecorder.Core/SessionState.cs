namespace VoiceRecorder.Core;

/// <summary>Estado de la máquina de estados de una sesión.</summary>
public enum SessionState
{
    Idle,

    /// <summary>Capturando y transcribiendo.</summary>
    Recording,

    /// <summary>En pausa: los timestamps no avanzan y no se procesa audio.</summary>
    Paused,

    /// <summary>Sesión finalizada.</summary>
    Stopped,
}
