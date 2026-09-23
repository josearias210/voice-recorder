using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceRecorder.Core;

namespace VoiceRecorder.Audio;

/// <summary>Captura el micrófono (WASAPI en modo compartido): la voz del usuario local.</summary>
public sealed class MicrophoneCaptureService : WasapiCaptureServiceBase
{
    public MicrophoneCaptureService(MMDevice? device = null) : base(device)
    {
        Source = AudioSource.Microphone;
    }

    protected override WasapiCapture CreateCapture(MMDevice? device)
    {
        return device is null ? new WasapiCapture() : new WasapiCapture(device);
    }
}
