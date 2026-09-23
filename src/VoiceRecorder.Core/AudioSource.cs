namespace VoiceRecorder.Core;

/// <summary>Origen de un flujo de audio capturado.</summary>
public enum AudioSource
{
    /// <summary>Micrófono: la voz del usuario local.</summary>
    Microphone,

    /// <summary>Loopback del dispositivo de salida: la voz de los participantes remotos.</summary>
    Loopback,
}
