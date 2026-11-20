// NAudio 3 marca WasapiCapture como obsoleta en favor de WasapiRecorder. Se mantiene
// la API probada con hardware real hasta migrar y verificar la captura con WasapiRecorder.
#pragma warning disable CS0618

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
