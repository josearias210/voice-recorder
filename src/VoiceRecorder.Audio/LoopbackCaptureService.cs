// NAudio 3 marca WasapiCapture como obsoleta en favor de WasapiRecorder. Se mantiene
// la API probada con hardware real hasta migrar y verificar la captura con WasapiRecorder.
#pragma warning disable CS0618

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
        // Sin audio reproduciéndose, WASAPI loopback no entrega muestras.
        FillSilenceGaps = true;
    }

    protected override WasapiCapture CreateCapture(MMDevice? device)
    {
        return device is null ? new WasapiLoopbackCapture() : new WasapiLoopbackCapture(device);
    }
}
