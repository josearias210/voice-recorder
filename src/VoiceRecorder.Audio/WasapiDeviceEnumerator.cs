using NAudio.CoreAudioApi;
using VoiceRecorder.Core;

namespace VoiceRecorder.Audio;

/// <summary>Enumera dispositivos WASAPI activos (salidas para loopback y micrófonos).</summary>
public sealed class WasapiDeviceEnumerator : IAudioDeviceEnumerator
{
    public IReadOnlyList<AudioDeviceInfo> ListOutputs() => List(DataFlow.Render);

    public IReadOnlyList<AudioDeviceInfo> ListMicrophones() => List(DataFlow.Capture);

    private static IReadOnlyList<AudioDeviceInfo> List(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<AudioDeviceInfo>();
        var defaultRole = Role.Console;

        var defaultId = TryGetDefaultId(enumerator, flow, defaultRole);
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            var name = device.ID == defaultId
                ? $"{device.FriendlyName} (por defecto)"
                : device.FriendlyName;
            var kind = flow == DataFlow.Render ? AudioDeviceKind.Output : AudioDeviceKind.Microphone;
            result.Add(new AudioDeviceInfo(device.ID, name, kind));
        }

        return result;
    }

    private static string? TryGetDefaultId(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(flow, role);
            return device.ID;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Resuelve un micrófono por Id; null devuelve el predeterminado del sistema.</summary>
    public static MMDevice? ResolveMicrophoneById(string? id)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        try
        {
            return enumerator.GetDevice(id);
        }
        catch
        {
            return null;
        }
    }
}
