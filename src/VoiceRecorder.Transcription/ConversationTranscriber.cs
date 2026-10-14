using System.Diagnostics;
using System.Threading.Channels;
using VoiceRecorder.Core;
using VoiceRecorder.Transcription.Vad;

namespace VoiceRecorder.Transcription;

/// <summary>
/// Orquesta captura dual + VAD + transcripción con máquina de estados de sesión
/// (Grabando ⇄ Pausada → Detenida). Los timestamps de los segmentos usan tiempo
/// activo, excluyendo las pausas.
/// Con motor nulo no se transcribe en vivo: solo captura (los bloques se
/// exponen por <see cref="AudioTapped"/> para volcarlos a archivo).
/// </summary>
public sealed class ConversationTranscriber : IDisposable
{
    private const int CaptureCount = 2;

    private readonly ITranscriptionEngine? _engine;
    private readonly Func<IVad> _vadFactory;
    private readonly IAudioCaptureService _microphone;
    private readonly IAudioCaptureService _loopback;
    private readonly Stopwatch _clock = new();
    private readonly Channel<Utterance> _queue = Channel.CreateUnbounded<Utterance>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    private SpeechSegmenter? _micSegmenter;
    private SpeechSegmenter? _loopSegmenter;
    private Task? _consumer;
    private CancellationTokenSource? _consumerCts;
    private TaskCompletionSource _capturesStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _stoppedCaptures;
    private TimeSpan _pausedAccum;
    private TimeSpan? _pauseStart;
    private SessionState _state = SessionState.Idle;
    private long _pendingUtterances;

    public ConversationTranscriber(
        ITranscriptionEngine? engine,
        Func<IVad> vadFactory,
        IAudioCaptureService microphone,
        IAudioCaptureService loopback)
    {
        _engine = engine;
        _vadFactory = vadFactory ?? throw new ArgumentNullException(nameof(vadFactory));
        _microphone = microphone ?? throw new ArgumentNullException(nameof(microphone));
        _loopback = loopback ?? throw new ArgumentNullException(nameof(loopback));
    }

    /// <summary>Estado actual de la sesión (respaldado por <see cref="_state"/>).</summary>
    public SessionState State => _state;

    /// <summary>Enunciados en cola esperando transcripción.</summary>
    public int PendingUtterances => (int)Interlocked.Read(ref _pendingUtterances);

    /// <summary>Tiempo activo de la sesión (sin contar pausas).</summary>
    public TimeSpan ActiveElapsed
    {
        get
        {
            var elapsed = _clock.Elapsed - _pausedAccum;
            if (_pauseStart is { } pauseStart)
            {
                elapsed -= _clock.Elapsed - pauseStart;
            }

            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }
    }

    public event Action<TranscriptSegment>? SegmentReady;

    public event Action<SessionState>? StateChanged;

    public event Action<Exception>? ErrorOccurred;

    /// <summary>Bloque de audio crudo capturado (para volcarlo a archivo). Hilo de captura.</summary>
    public event Action<AudioChunk>? AudioTapped;

    /// <summary>Canal opcional de diagnóstico (hilos de fondo).</summary>
    public Action<string>? Trace;

    public void Start()
    {
        if (_state is SessionState.Recording or SessionState.Paused)
        {
            return;
        }

        if (_engine is not null)
        {
            _micSegmenter = CreateSegmenter(Speaker.Me);
            _loopSegmenter = CreateSegmenter(Speaker.Them);
            _consumerCts = new CancellationTokenSource();
            _consumer = Task.Run(() => ConsumeAsync(_consumerCts.Token));
        }

        HookCaptures();
        ResetCaptureStopWait();

        _microphone.Start();
        _loopback.Start();

        _pausedAccum = TimeSpan.Zero;
        _pauseStart = null;
        _clock.Reset();
        _clock.Start();
        SetState(SessionState.Recording);
    }

    public void Pause()
    {
        if (_state != SessionState.Recording)
        {
            return;
        }

        _pauseStart = _clock.Elapsed;
        StopCaptures();
        FlushSegmenters();
        SetState(SessionState.Paused);
    }

    public void Resume()
    {
        if (_state != SessionState.Paused)
        {
            return;
        }

        if (_pauseStart is { } pauseStart)
        {
            _pausedAccum += _clock.Elapsed - pauseStart;
            _pauseStart = null;
        }

        // Reinicia el estado de VAD/segmentación sin recrear sesiones de
        // inferencia (recargar Silero bloquearía el hilo llamador).
        _micSegmenter?.Reset();
        _loopSegmenter?.Reset();
        ResetCaptureStopWait();
        _microphone.Start();
        _loopback.Start();
        SetState(SessionState.Recording);
    }

    public async Task StopAsync()
    {
        if (_state is SessionState.Idle or SessionState.Stopped)
        {
            return;
        }

        StopCaptures();
        // Espera el drenaje final de ambas capturas: sus últimos bloques se
        // entregan justo antes de sus eventos Stopped (máx. 2s por seguridad).
        try
        {
            await _capturesStopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            // Captura colgada (dispositivo desaparecido): seguimos con lo recibido.
        }

        FlushSegmenters();
        if (_consumer is not null)
        {
            _queue.Writer.TryComplete();
            try
            {
                await _consumer.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch
            {
                // Tiempo agotado o cancelación: se abandona el drenaje pendiente.
            }
        }

        if (_pauseStart is { } pauseStart)
        {
            _pausedAccum += _clock.Elapsed - pauseStart;
            _pauseStart = null;
        }

        _clock.Stop();
        SetState(SessionState.Stopped);
        CleanupSegmenters();
    }

    public void Dispose()
    {
        if (_state is SessionState.Recording or SessionState.Paused)
        {
            StopCaptures();
            _queue.Writer.TryComplete();
        }

        _consumerCts?.Cancel();
        _consumerCts?.Dispose();
        CleanupSegmenters();
        // Las capturas se crean por sesión y este objeto es su único dueño:
        // sin esto se acumulan dispositivos COM (MMDevice) por cada sesión.
        try
        {
            _microphone.Dispose();
        }
        catch
        {
            // Dispositivo ya desaparecido.
        }

        try
        {
            _loopback.Dispose();
        }
        catch
        {
            // Dispositivo ya desaparecido.
        }

        _clock.Stop();
        GC.SuppressFinalize(this);
    }

    private void OnChunk(object? sender, AudioChunk chunk)
    {
        if (_state != SessionState.Recording)
        {
            return;
        }

        try
        {
            AudioTapped?.Invoke(chunk);
            if (_engine is null)
            {
                return;
            }

            var activeTime = ActiveElapsed;
            var segmenter = chunk.Source == AudioSource.Microphone ? _micSegmenter : _loopSegmenter;
            Trace?.Invoke($"[chunk {chunk.Source}] t={activeTime:hh\\:mm\\:ss\\.ff} n={chunk.Samples.Length}");
            segmenter?.Feed(chunk.Samples, activeTime);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
        }
    }

    private void HookCaptures()
    {
        _microphone.AudioChunkReady += OnChunk;
        _loopback.AudioChunkReady += OnChunk;
        _microphone.Stopped += OnCaptureStopped;
        _microphone.CaptureFailed += (_, _) => OnCaptureStopped(_microphone, EventArgs.Empty);
        _loopback.Stopped += OnCaptureStopped;
        _loopback.CaptureFailed += (_, _) => OnCaptureStopped(_loopback, EventArgs.Empty);
    }

    private void OnCaptureStopped(object? sender, EventArgs e)
    {
        if (Interlocked.Increment(ref _stoppedCaptures) >= CaptureCount)
        {
            _capturesStopped.TrySetResult();
        }
    }

    private void ResetCaptureStopWait()
    {
        _stoppedCaptures = 0;
        _capturesStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void OnUtterance(Utterance utterance)
    {
        Interlocked.Increment(ref _pendingUtterances);
        _queue.Writer.TryWrite(utterance);
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (var utterance in _queue.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                var segments = await _engine!.TranscribeAsync(utterance, cancellationToken);
                foreach (var segment in segments)
                {
                    SegmentReady?.Invoke(segment);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingUtterances);
            }
        }
    }

    private SpeechSegmenter CreateSegmenter(Speaker speaker)
    {
        var segmenter = new SpeechSegmenter(_vadFactory(), speaker)
        {
            Trace = Trace,
        };
        segmenter.UtteranceCompleted += OnUtterance;
        return segmenter;
    }

    private void FlushSegmenters()
    {
        try
        {
            _micSegmenter?.Flush();
            _loopSegmenter?.Flush();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
        }
    }

    private void CleanupSegmenters()
    {
        _micSegmenter?.Dispose();
        _loopSegmenter?.Dispose();
        _micSegmenter = null;
        _loopSegmenter = null;
    }

    private void StopCaptures()
    {
        _microphone.Stop();
        _loopback.Stop();
    }

    private void SetState(SessionState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }
}
