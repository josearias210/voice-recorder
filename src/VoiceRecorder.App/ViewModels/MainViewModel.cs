using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VoiceRecorder.App.Infrastructure;
using VoiceRecorder.Audio;
using VoiceRecorder.Core;
using VoiceRecorder.Transcription;
using VoiceRecorder.Transcription.Vad;

namespace VoiceRecorder.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private static readonly (string Code, string Name)[] Languages =
    [
        ("es", "Español"),
        ("en", "English"),
        ("auto", "Detección automática"),
    ];

    private readonly ISessionStore _store;
    private readonly ModelManager _models;
    private readonly IAudioDeviceEnumerator _devices;
    private readonly SynchronizationContext _ui;
    private readonly DispatcherTimer _timer;

    private ConversationTranscriber? _transcriber;
    private WhisperTranscriber? _engine;
    private string? _engineKey;
    private DateTime _sessionStart;
    private readonly List<TranscriptSegment> _liveSegments = [];
    private SessionState _state = SessionState.Idle;

    public MainViewModel(ISessionStore store, ModelManager models, IAudioDeviceEnumerator devices)
    {
        _store = store;
        _models = models;
        _devices = devices;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _timer.Tick += (_, _) => UpdateElapsed();

        ModelOptions = ModelManager.Catalog;
        SelectedModel = ModelOptions.First(m => m.FileName == "ggml-base-q5_1.bin");
        LanguageOptions = Languages;
        LoadDevices();
        LoadHistory(null);

        StartCommand = new AsyncRelayCommand(_ => StartSessionAsync(), _ => CanStart);
        PauseResumeCommand = new RelayCommand(_ => TogglePause(), _ => CanPauseResume);
        StopCommand = new AsyncRelayCommand(_ => StopSessionAsync(), _ => CanStop);
        DownloadModelCommand = new AsyncRelayCommand(_ => DownloadModelAsync(), _ => CanDownloadModel);
        SearchCommand = new RelayCommand(_ => LoadHistory(SearchText));
    }

    public event Action? SessionChanged;

    public IReadOnlyList<WhisperModelInfo> ModelOptions { get; }

    public IReadOnlyList<(string Code, string Name)> LanguageOptions { get; }

    public ObservableCollection<AudioDeviceInfo> MicrophoneOptions { get; } = [];

    public ObservableCollection<SegmentVm> Segments { get; } = [];

    public ObservableCollection<SessionRowVm> History { get; } = [];

    private WhisperModelInfo _selectedModel;

    public WhisperModelInfo SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (Set(ref _selectedModel, value))
            {
                RefreshModelAvailability();
            }
        }
    }

    private string _selectedLanguageCode = "es";

    public string SelectedLanguageCode
    {
        get => _selectedLanguageCode;
        set => Set(ref _selectedLanguageCode, value);
    }

    public AudioDeviceInfo? SelectedMicrophone { get; set; }

    public string? SearchText { get; set; }

    public RelayCommand SearchCommand { get; }

    public AsyncRelayCommand StartCommand { get; }

    public RelayCommand PauseResumeCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public AsyncRelayCommand DownloadModelCommand { get; }

    public SessionState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(PauseButtonText));
                SessionChanged?.Invoke();
            }
        }
    }

    public string StateText => State switch
    {
        SessionState.Recording => "● Grabando",
        SessionState.Paused => "⏸ En pausa",
        SessionState.Stopped => "Sesión detenida",
        _ => "Listo",
    };

    public string PauseButtonText => State == SessionState.Paused ? "Reanudar" : "Pausar";

    private string _statusText = "Listo para grabar. Las llamadas se transcriben en vivo (sin audio guardado).";

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    private double _downloadPercent = -1;

    public double DownloadPercent
    {
        get => _downloadPercent;
        private set
        {
            if (Set(ref _downloadPercent, value))
            {
                OnPropertyChanged(nameof(DownloadVisible));
            }
        }
    }

    public bool DownloadVisible => DownloadPercent is >= 0 and < 1;

    private string _elapsedText = "00:00:00";

    public string ElapsedText
    {
        get => _elapsedText;
        private set => Set(ref _elapsedText, value);
    }

    private bool CanStart => State is SessionState.Idle or SessionState.Stopped;

    private bool CanPauseResume => State is SessionState.Recording or SessionState.Paused;

    private bool CanStop => State is SessionState.Recording or SessionState.Paused;

    private bool CanDownloadModel => !_models.IsAvailable(SelectedModel);

    /// <summary>Reevalúa la disponibilidad del modelo seleccionado (para bindings).</summary>
    public void RefreshModelAvailability()
    {
        OnPropertyChanged(nameof(CanDownloadModel));
        CommandManager.InvalidateRequerySuggested();
    }

    public bool HasUnsavedLiveSession => State is SessionState.Recording or SessionState.Paused;

    private void LoadDevices()
    {
        MicrophoneOptions.Clear();
        foreach (var device in _devices.ListMicrophones())
        {
            MicrophoneOptions.Add(device);
        }

        SelectedMicrophone = MicrophoneOptions.FirstOrDefault(d => d.Name.Contains("por defecto", StringComparison.OrdinalIgnoreCase))
                             ?? MicrophoneOptions.FirstOrDefault();
    }

    public void LoadHistory(string? search)
    {
        History.Clear();
        foreach (var summary in _store.ListSessions(search))
        {
            History.Add(new SessionRowVm(summary));
        }
    }

    private async Task StartSessionAsync()
    {
        try
        {
            StatusText = "Preparando modelos…";
            DownloadPercent = 0;
            var modelPath = await _models.EnsureWhisperModelAsync(
                SelectedModel,
                new Progress<double>(p => _ui.Post(_ => DownloadPercent = p, null)),
                CancellationToken.None);
            await _models.EnsureVadModelAsync(CancellationToken.None);
            DownloadPercent = 1;

            var engineKey = $"{modelPath}|{SelectedLanguageCode}";
            if (_engine is null || _engineKey != engineKey)
            {
                _engine?.Dispose();
                _engine = new WhisperTranscriber(modelPath, SelectedLanguageCode);
                _engineKey = engineKey;
            }

            string vadError = string.Empty;
            IVad VadFactory()
            {
                try
                {
                    return SileroVad.FromFile(_models.VadModelPath);
                }
                catch (Exception ex)
                {
                    vadError = ex.Message;
                    return new EnergyVad();
                }
            }

            var microphone = new MicrophoneCaptureService(WasapiDeviceEnumerator.ResolveMicrophoneById(SelectedMicrophone?.Id));
            var loopback = new LoopbackCaptureService();
            _transcriber = new ConversationTranscriber(_engine, VadFactory, microphone, loopback);
            _transcriber.SegmentReady += OnSegmentReady;
            _transcriber.ErrorOccurred += ex => _ui.Post(_ => StatusText = $"Error: {ex.Message}", null);

            _liveSegments.Clear();
            Segments.Clear();
            _sessionStart = DateTime.Now;
            _transcriber.Start();

            _timer.Start();
            StatusText = string.IsNullOrEmpty(vadError)
                ? "Transcripción en vivo (Silero VAD). El audio no se guarda."
                : $"Transcripción en vivo (VAD por energía: {vadError}). El audio no se guarda.";
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo iniciar: {ex.Message}";
            DownloadPercent = -1;
        }
        finally
        {
            OnPropertyChanged(nameof(CanStart));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void TogglePause()
    {
        if (_transcriber is null)
        {
            return;
        }

        if (State == SessionState.Recording)
        {
            _transcriber.Pause();
            StatusText = "En pausa. El cronómetro está detenido.";
        }
        else if (State == SessionState.Paused)
        {
            _transcriber.Resume();
            StatusText = "Transcripción en vivo. El audio no se guarda.";
        }
    }

    private async Task StopSessionAsync()
    {
        if (_transcriber is null)
        {
            return;
        }

        var transcriber = _transcriber;
        _transcriber = null;
        try
        {
            StatusText = "Deteniendo y guardando…";
            await transcriber.StopAsync();
            _timer.Stop();

            IReadOnlyList<TranscriptSegment> segments;
            lock (_liveSegments)
            {
                segments = [.. _liveSegments];
            }

            var title = $"Llamada {_sessionStart:dd/MM HH:mm}";
            var id = await Task.Run(() => _store.SaveSession(
                title,
                _sessionStart,
                transcriber.ActiveElapsed,
                string.Empty,
                string.Empty,
                segments));

            StatusText = $"Sesión guardada ({segments.Count} segmentos).";
            Segments.Clear();
            LoadHistory(SearchText);
        }
        catch (Exception ex)
        {
            StatusText = $"Error al detener: {ex.Message}";
        }
        finally
        {
            transcriber.Dispose();
            OnPropertyChanged(nameof(CanStart));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task DownloadModelAsync()
    {
        try
        {
            StatusText = $"Descargando {SelectedModel.DisplayName}…";
            DownloadPercent = 0;
            await _models.EnsureWhisperModelAsync(
                SelectedModel,
                new Progress<double>(p => _ui.Post(_ => DownloadPercent = p, null)),
                CancellationToken.None);
            StatusText = "Modelo descargado.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error descargando modelo: {ex.Message}";
        }
        finally
        {
            DownloadPercent = -1;
            OnPropertyChanged(nameof(CanDownloadModel));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public async Task StopIfActiveAsync()
    {
        if (_transcriber is not null && HasUnsavedLiveSession)
        {
            await StopSessionAsync();
        }
    }

    private void OnSegmentReady(TranscriptSegment segment)
    {
        lock (_liveSegments)
        {
            _liveSegments.Add(segment);
        }

        _ui.Post(_ =>
        {
            var vm = new SegmentVm(segment);
            var index = 0;
            while (index < Segments.Count && Segments[index].TimeText.CompareTo(vm.TimeText) <= 0)
            {
                index++;
            }

            Segments.Insert(index, vm);
        }, null);
    }

    private void UpdateElapsed()
    {
        if (_transcriber is not null)
        {
            ElapsedText = _transcriber.ActiveElapsed.ToString(@"hh\:mm\:ss");
        }
    }
}
