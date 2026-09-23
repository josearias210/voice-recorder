namespace VoiceRecorder.Core;

/// <summary>Hablante atribuido a un segmento de transcripción.</summary>
public enum Speaker
{
    Unknown,

    /// <summary>Voz detectada en el canal del micrófono.</summary>
    Me,

    /// <summary>Voz detectada en el canal de loopback (participantes remotos).</summary>
    Them,

    /// <summary>Voz simultánea en ambos canales.</summary>
    Both,
}
