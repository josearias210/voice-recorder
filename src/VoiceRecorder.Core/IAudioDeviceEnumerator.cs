namespace VoiceRecorder.Core;

public enum AudioDeviceKind
{
    Output,
    Microphone,
}

/// <summary>Dispositivo de audio del sistema.</summary>
/// <param name="Id">Identificador estable del dispositivo (MMDevice ID).</param>
/// <param name="Name">Nombre amigable.</param>
/// <param name="Kind">Tipo de dispositivo.</param>
public sealed record AudioDeviceInfo(string Id, string Name, AudioDeviceKind Kind);

/// <summary>Enumera los dispositivos de audio disponibles.</summary>
public interface IAudioDeviceEnumerator
{
    /// <summary>Dispositivo de salida por defecto + resto de salidas (para loopback).</summary>
    IReadOnlyList<AudioDeviceInfo> ListOutputs();

    /// <summary>Micrófonos disponibles.</summary>
    IReadOnlyList<AudioDeviceInfo> ListMicrophones();
}
