using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceRecorder.Core;

namespace VoiceRecorder.Audio;

/// <summary>
/// Base para servicios de captura WASAPI. Normaliza el audio del dispositivo
/// (formato nativo compartido) a mono 16kHz float mediante resampleo continuo,
/// y emite <see cref="AudioChunk"/> periódicamente.
/// </summary>
public abstract class WasapiCaptureServiceBase : IAudioCaptureService
{
    private const int PollIntervalMs = 50;
    private const int MaxReadSamples = 8192;
    private const int SourceBufferSeconds = 15;

    private readonly object _gate = new();
    private readonly MMDevice? _device;
    private WasapiCapture? _capture;
    private BufferedWaveProvider? _sourceBuffer;
    private MonoSampleProvider? _monoPipeline;
    private Timer? _pollTimer;
    private long _totalSamplesEmitted;
    private int _draining;
    private volatile bool _capturing;

    protected WasapiCaptureServiceBase(MMDevice? device) => _device = device;

    public AudioSource Source { get; protected init; }

    public bool IsCapturing => _capturing;

    public event EventHandler<AudioChunk>? AudioChunkReady;

    public event EventHandler? Stopped;

    public event EventHandler<Exception>? CaptureFailed;

    protected abstract WasapiCapture CreateCapture(MMDevice? device);

    public void Start()
    {
        lock (_gate)
        {
            if (_capturing)
            {
                return;
            }

            _capture = CreateCapture(_device);
            var format = _capture.WaveFormat;
            _sourceBuffer = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromSeconds(SourceBufferSeconds),
                ReadFully = false,
                DiscardOnBufferOverflow = true,
            };
            var floatSource = _sourceBuffer.ToSampleProvider();
            var resampler = new WdlResamplingSampleProvider(floatSource, AudioConstants.TargetSampleRate);
            _monoPipeline = new MonoSampleProvider(resampler);
            _totalSamplesEmitted = 0;
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _capturing = true;
            _pollTimer = new Timer(DrainBuffer, null, PollIntervalMs, PollIntervalMs);
        }
    }

    public void Stop()
    {
        WasapiCapture? capture;
        lock (_gate)
        {
            if (!_capturing || _capture is null)
            {
                return;
            }

            capture = _capture;
        }

        capture.StopRecording();
    }

    public void Dispose()
    {
        try
        {
            Stop();
        }
        catch
        {
            // El dispositivo pudo haber desaparecido; continuamos con la liberación.
        }

        lock (_gate)
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
            _capture?.Dispose();
            _capture = null;
            _sourceBuffer = null;
            _monoPipeline = null;
        }

        GC.SuppressFinalize(this);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_gate)
        {
            _sourceBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        try
        {
            DrainBuffer(null);
        }
        finally
        {
            lock (_gate)
            {
                _pollTimer?.Dispose();
                _pollTimer = null;
                _capture = null;
                _capturing = false;
            }

            if (e.Exception is not null)
            {
                CaptureFailed?.Invoke(this, e.Exception);
            }
            else
            {
                Stopped?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void DrainBuffer(object? state)
    {
        if (Interlocked.Exchange(ref _draining, 1) == 1)
        {
            return;
        }

        try
        {
            while (true)
            {
                float[] output;
                int read;
                lock (_gate)
                {
                    if (_monoPipeline is null || !_capturing)
                    {
                        break;
                    }

                    output = new float[MaxReadSamples];
                    read = _monoPipeline.Read(output, 0, MaxReadSamples);
                }

                if (read <= 0)
                {
                    break;
                }

                var samples = new float[read];
                Array.Copy(output, samples, read);
                long timestampSamples;
                lock (_gate)
                {
                    timestampSamples = _totalSamplesEmitted;
                    _totalSamplesEmitted += read;
                }

                var chunk = new AudioChunk(
                    Source,
                    samples,
                    TimeSpan.FromSeconds((double)timestampSamples / AudioConstants.TargetSampleRate));
                AudioChunkReady?.Invoke(this, chunk);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _draining, 0);
        }
    }
}
