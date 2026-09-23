using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceRecorder.Core;

namespace VoiceRecorder.Audio;

/// <summary>
/// Captura el audio que el sistema reproduce por el dispositivo de salida
/// (WASAPI Loopback): la voz de los participantes remotos de la llamada.
/// Funciona con cualquier aplicación sin drivers adicionales.
/// </summary>
public sealed class LoopbackCaptureService : WasapiCaptureServiceBase
{
    public LoopbackCaptureService(MMDevice? device = null) : base(device)
    {
        Source = AudioSource.Loopback;
    }

    protected override WasapiCapture CreateCapture(MMDevice? device)
    {
        return device is null ? new WasapiLoopbackCapture() : new WasapiLoopbackCapture(device);
    }
}
