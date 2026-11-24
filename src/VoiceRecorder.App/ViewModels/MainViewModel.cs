using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VoiceRecorder.App.Infrastructure;
using VoiceRecorder.App.Services;
using VoiceRecorder.Audio;
using VoiceRecorder.Core;
using VoiceRecorder.Storage;
using VoiceRecorder.Transcription;
using VoiceRecorder.Transcription.Vad;

namespace VoiceRecorder.App.ViewModels;

/// <summary>Idioma de transcripción seleccionable en la UI.</summary>
/// <param name="Code">Código para Whisper ("es", "en", "auto"...).</param>
/// <param name="Name">Etiqueta visible.</param>
public sealed record LanguageInfo(string Code, string Name);

/// <summary>Tema visual seleccionable en la UI.</summary>
/// <param name="Theme">Valor del enum.</param>
/// <param name="Name">Etiqueta visible.</param>
public sealed record ThemeOption(AppTheme Theme, string Name);

public sealed class MainViewModel : ViewModelBase
{
    private static readonly LanguageInfo[] Languages =
    [
        new("es", "Español"),
        new("en", "English"),
        new("auto", "Detección automática"),
    ];

    private readonly ISessionStore _store;
    private readonly ModelManager _models;
    private readonly IAudioDeviceEnumerator _devices;
    private readonly ThemeService? _themes;
    private readonly SettingsService? _settings;
    private readonly UpdateService _updates = new();
    private readonly TranscriptionLog? _costLog;

    /// <summary>Cliente HTTP compartido para la API de OpenAI (un bloque puede tardar).</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly SynchronizationContext _ui;
    private readonly DispatcherTimer _timer;

    private ConversationTranscriber? _transcriber;
    private WhisperTranscriber? _engine;
    private string? _engineKey;
    private DateTime _sessionStart;
    private SessionState _state = SessionState.Idle;
    private int _importGuard;
    private CancellationTokenSource? _importCts;
    private readonly bool _settingsLoaded;
    private WavChunkWriter? _micWriter;
    private WavChunkWriter? _loopWriter;
    private bool _isFinalizing;
    private bool _isBusy;

    public MainViewModel(
        ISessionStore store,
        ModelManager models,
        IAudioDeviceEnumerator devices,
        ThemeService? themes = null,
        SettingsService? settings = null,
        TranscriptionLog? costLog = null)
    {
        _store = store;
        _models = models;
        _devices = devices;
        _themes = themes;
        _settings = settings;
        _costLog = costLog;
        _settings?.Load();
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _selectedTheme = themes?.Current ?? AppTheme.Dark;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _timer.Tick += (_, _) => UpdateElapsed();

        ModelOptions = ModelManager.Catalog;
        LanguageOptions = Languages;
        if (_settings?.LanguageCode is { } savedLanguage && Languages.Any(l => l.Code == savedLanguage))
        {
            _selectedLanguageCode = savedLanguage;
        }

        SelectedModel = ModelOptions.FirstOrDefault(m => m.FileName == _settings?.ModelFileName)
                        ?? ModelOptions.First(m => m.FileName == "ggml-base-q5_1.bin");

        LoadDevices();
        _settingsLoaded = true;
        LoadHistory(null);

        StartCommand = new AsyncRelayCommand(_ => StartSessionAsync(), _ => CanStart);
        PauseResumeCommand = new RelayCommand(_ => TogglePause(), _ => CanPauseResume);
        StopCommand = new AsyncRelayCommand(_ => StopSessionAsync(), _ => CanStop);
        TranscribeFileCommand = new AsyncRelayCommand(_ => TranscribeFilesAsync(), _ => CanStart);
        CancelImportCommand = new RelayCommand(_ => _importCts?.Cancel(), _ => IsImporting);
        DownloadModelCommand = new AsyncRelayCommand(_ => DownloadModelAsync(), _ => CanDownloadModel);
        UpdateNowCommand = new AsyncRelayCommand(_ => UpdateNowAsync(), _ => UpdateAvailable && CanStart);
        CheckForUpdatesCommand = new AsyncRelayCommand(_ => CheckForUpdatesAsync(manual: true), _ => CanStart && !_checkingUpdates);
        SearchCommand = new RelayCommand(_ => LoadHistory(SearchText));
    }

    public event Action? SessionChanged;

    /// <summary>Sesión importada y guardada (Id) — para abrir su chat automáticamente.</summary>
    public event Action<long>? SessionImported;

    public IReadOnlyList<WhisperModelInfo> ModelOptions { get; }

    public IReadOnlyList<LanguageInfo> LanguageOptions { get; }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(AppTheme.Dark, "Oscuro"),
        new(AppTheme.Light, "Claro"),
    ];

    public ObservableCollection<AudioDeviceInfo> MicrophoneOptions { get; } = [];

    public ObservableCollection<SegmentVm> Segments { get; } = [];

    public ObservableCollection<SessionRowVm> History { get; } = [];

    private WhisperModelInfo _selectedModel = null!;

    public WhisperModelInfo SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (Set(ref _selectedModel, value))
            {
                RefreshModelAvailability();
                PersistSettings();
            }
        }
    }

    private string _selectedLanguageCode = "es";

    public string SelectedLanguageCode
    {
        get => _selectedLanguageCode;
        set
        {
            if (Set(ref _selectedLanguageCode, value))
            {
                PersistSettings();
            }
        }
    }

    private AppTheme _selectedTheme;

    public AppTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (Set(ref _selectedTheme, value) && _themes is not null && _themes.Current != value)
            {
                _themes.SetTheme(value);
            }
        }
    }

    private AudioDeviceInfo? _selectedMicrophone;

    public AudioDeviceInfo? SelectedMicrophone
    {
        get => _selectedMicrophone;
        set
        {
            if (Set(ref _selectedMicrophone, value))
            {
                PersistSettings();
            }
        }
    }

    /// <summary>
    /// Pregunta al usuario si transcribir y con qué motor (lo asigna la
    /// ventana). Devuelve null si decide no transcribir por ahora.
    /// </summary>
    public Func<TranscriptionPrompt, TranscriptionEngineKind?>? ChooseEngine { get; set; }

    /// <summary>Registro de transcripciones (tiempo y coste) para la ventana «Costes…».</summary>
    public TranscriptionLog? CostLog => _costLog;

    /// <summary>
    /// Carpeta donde se escriben los WAV mientras se
    /// graba; al guardar la sesión se mueven a la carpeta de la sesión.
    /// </summary>
    public static string AudioDirectory => Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceRecorder",
        "audio");

    /// <summary>Indica si se está deteniendo, transcribiendo o guardando la sesión.</summary>
    public bool IsFinalizing
    {
        get => _isFinalizing;
        private set
        {
            if (Set(ref _isFinalizing, value))
            {
                OnPropertyChanged(nameof(CanStart));
                CommandManager.InvalidateRequerySuggested();
                SessionChanged?.Invoke();
            }
        }
    }

    public string? SearchText { get; set; }

    /// <summary>Indica si se preparan modelos o se descarga una actualización.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanStart));
                CommandManager.InvalidateRequerySuggested();
                SessionChanged?.Invoke();
            }
        }
    }

    public RelayCommand SearchCommand { get; }

    public AsyncRelayCommand StartCommand { get; }

    public RelayCommand PauseResumeCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public AsyncRelayCommand TranscribeFileCommand { get; }

    public RelayCommand CancelImportCommand { get; }

    public AsyncRelayCommand DownloadModelCommand { get; }

    public AsyncRelayCommand UpdateNowCommand { get; }

    /// <summary>Busca actualizaciones a petición del usuario.</summary>
    public AsyncRelayCommand CheckForUpdatesCommand { get; }

    /// <summary>Versión instalada (por ejemplo, "1.3.0").</summary>
    public string AppVersion => AppInfo.Version;

    /// <summary>Texto de versión para la barra de estado: "v1.3.0 · instalada" o "v1.3.0 · portable".</summary>
    public string VersionText => $"v{AppVersion} · {(_updates.IsSupported ? "instalada" : "portable")}";

    public string WindowTitle => $"Voice Recorder {AppVersion} · Transcriptor de llamadas";

    private bool _checkingUpdates;

    private bool _updateAvailable;

    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set
        {
            if (Set(ref _updateAvailable, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private string _updateVersion = string.Empty;

    public string UpdateVersion
    {
        get => _updateVersion;
        private set => Set(ref _updateVersion, value);
    }

    public SessionState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(PauseButtonText));
                OnPropertyChanged(nameof(HasUnsavedLiveSession));
                OnPropertyChanged(nameof(CanStart));
                CommandManager.InvalidateRequerySuggested();
                SessionChanged?.Invoke();
            }
        }
    }

    public string StateText => State switch
    {
        SessionState.Recording => "Grabando",
        SessionState.Paused => "En pausa",
        SessionState.Stopped => "Sesión detenida",
        _ => "Listo",
    };

    public string PauseButtonText => State == SessionState.Paused ? "Reanudar" : "Pausar";

    private string _statusText = "Listo para grabar. El audio se guarda y al detener eliges si transcribirlo.";

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

    private bool _isImporting;

    /// <summary>Indica si hay una importación de archivos en curso.</summary>
    public bool IsImporting
    {
        get => _isImporting;
        private set
        {
            if (Set(ref _isImporting, value))
            {
                OnPropertyChanged(nameof(CanCancelImport));
                OnPropertyChanged(nameof(CanStart));
                CommandManager.InvalidateRequerySuggested();
                SessionChanged?.Invoke();
            }
        }
    }

    public bool CanCancelImport => IsImporting;

    private string _elapsedText = "00:00:00";

    public string ElapsedText
    {
        get => _elapsedText;
        private set => Set(ref _elapsedText, value);
    }

    /// <summary>True si se puede iniciar una sesión (usado también por bindings de la UI).</summary>
    public bool CanStart => State is SessionState.Idle or SessionState.Stopped && !IsImporting && !IsFinalizing && !IsBusy;

    private bool CanPauseResume => State is SessionState.Recording or SessionState.Paused && !IsFinalizing;

    private bool CanStop => State is SessionState.Recording or SessionState.Paused && !IsFinalizing;

    private bool CanDownloadModel => CanStart && !_models.IsAvailable(SelectedModel);

    /// <summary>Etiqueta del botón de descarga: refleja si el modelo ya está en caché.</summary>
    public string DownloadButtonText => _models.IsAvailable(SelectedModel) ? "Descargado" : "Descargar";

    /// <summary>Reevalúa la disponibilidad del modelo seleccionado (para bindings).</summary>
    public void RefreshModelAvailability()
    {
        OnPropertyChanged(nameof(CanDownloadModel));
        OnPropertyChanged(nameof(DownloadButtonText));
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

        // Asignación directa: durante la carga no se persiste (p. ej. si el
        // micrófono guardado está temporalmente desconectado).
        _selectedMicrophone = MicrophoneOptions.FirstOrDefault(d => d.Id == _settings?.MicrophoneId)
                              ?? MicrophoneOptions.FirstOrDefault(d => d.Name.Contains("por defecto", StringComparison.OrdinalIgnoreCase))
                              ?? MicrophoneOptions.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedMicrophone));
    }

    public void LoadHistory(string? search)
    {
        History.Clear();
        foreach (var summary in _store.ListSessions(search))
        {
            History.Add(new SessionRowVm(summary));
        }
    }

    /// <summary>Guarda las preferencias de modelo, idioma y micrófono.</summary>
    private void PersistSettings()
    {
        if (_settings is null || !_settingsLoaded)
        {
            return; // Durante la restauración inicial no se sobrescriben valores aún no cargados.
        }

        _settings.ModelFileName = SelectedModel?.FileName;
        _settings.LanguageCode = SelectedLanguageCode;
        _settings.MicrophoneId = SelectedMicrophone?.Id;
        _settings.Save();
    }

    /// <summary>Garantiza los modelos y el motor Whisper según la selección actual (con caché).</summary>
    private async Task<WhisperTranscriber> EnsureEngineAsync(CancellationToken cancellationToken = default)
    {
        var modelPath = await _models.EnsureWhisperModelAsync(
            SelectedModel,
            new Progress<double>(p => _ui.Post(_ => DownloadPercent = p, null)),
            cancellationToken);
        await _models.EnsureVadModelAsync(cancellationToken);

        var engineKey = $"{modelPath}|{SelectedLanguageCode}";
        if (_engine is null || _engineKey != engineKey)
        {
            var language = SelectedLanguageCode;
            _engine?.Dispose();
            // Cargar el modelo GGML bloquea cientos de ms: fuera del hilo de UI.
            _engine = await Task.Run(() => new WhisperTranscriber(modelPath, language), cancellationToken);
            _engineKey = engineKey;
        }

        return _engine;
    }

    /// <summary>Fábrica de VAD Silero con repliegue a VAD por energía.</summary>
    private Func<IVad> CreateVadFactory(Action<string> onError) => () =>
    {
        try
        {
            return SileroVad.FromFile(_models.VadModelPath);
        }
        catch (Exception ex)
        {
            onError(ex.Message);
            return new EnergyVad();
        }
    };

    /// <summary>
    /// Precrea los VAD (micrófono y loopback) fuera del hilo de UI para que
    /// <see cref="ConversationTranscriber.Start"/> no congele la ventana.
    /// </summary>
    private async Task<Func<IVad>> PreloadVadsAsync(int count, Action<string> onError)
    {
        var factory = CreateVadFactory(onError);
        var preloaded = new Queue<IVad>(count);
        for (var i = 0; i < count; i++)
        {
            preloaded.Enqueue(await Task.Run(factory));
        }

        return () => preloaded.Count > 0 ? preloaded.Dequeue() : factory();
    }

    private async Task StartSessionAsync()
    {
        if (!CanStart)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // Siempre se graba primero: no se cargan modelos y el arranque es inmediato.
            StatusText = "Preparando captura de audio…";
            _sessionStart = DateTime.Now;

            var microphone = new MicrophoneCaptureService(WasapiDeviceEnumerator.ResolveMicrophoneById(SelectedMicrophone?.Id));
            var loopback = new LoopbackCaptureService();
            // Sin motor: el transcriptor solo captura (el VAD no se usa).
            var transcriber = new ConversationTranscriber(null, () => new EnergyVad(), microphone, loopback);
            // Se asigna ya: si algo falla después, el catch puede liberarlo
            // (Dispose también cierra las capturas).
            _transcriber = transcriber;
            transcriber.StateChanged += state => _ui.Post(
                _ =>
                {
                    State = state;
                    CommandManager.InvalidateRequerySuggested();
                },
                null);
            transcriber.ErrorOccurred += ex => _ui.Post(_ => StatusText = $"Error: {ex.Message}", null);

            var baseName = $"grabacion-{_sessionStart:yyyyMMdd-HHmmss}";
            _micWriter = new WavChunkWriter(Path.Join(AudioDirectory, $"{baseName}{MicSuffix}"));
            _loopWriter = new WavChunkWriter(Path.Join(AudioDirectory, $"{baseName}{CallSuffix}"));
            transcriber.AudioTapped += OnAudioTapped;

            Segments.Clear();
            _transcriber.Start();

            _timer.Start();
            StatusText = "Grabando audio (micrófono y llamada por separado). Al detener podrás transcribirlo.";
        }
        catch (Exception ex)
        {
            // Libera lo creado (WAV abiertos, capturas) para poder reintentar.
            DisposeAudioWriters();
            Interlocked.Exchange(ref _transcriber, null)?.Dispose();
            StatusText = $"No se pudo iniciar: {ex.Message}";
            DownloadPercent = -1;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanStart));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Vuelca cada bloque capturado al WAV de su canal (hilo de captura).</summary>
    private void OnAudioTapped(AudioChunk chunk)
    {
        try
        {
            if (chunk.Source == AudioSource.Microphone)
            {
                _micWriter?.WriteSamples(chunk.Samples);
            }
            else
            {
                _loopWriter?.WriteSamples(chunk.Samples);
            }
        }
        catch (Exception ex)
        {
            _ui.Post(_ => StatusText = $"Error grabando audio: {ex.Message}", null);
        }
    }

    /// <summary>Cierra los WAV en curso, finalizando sus cabeceras.</summary>
    private void DisposeAudioWriters()
    {
        var mic = Interlocked.Exchange(ref _micWriter, null);
        mic?.Dispose();
        var loop = Interlocked.Exchange(ref _loopWriter, null);
        loop?.Dispose();
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
            StatusText = "Grabando audio. Al detener podrás transcribirlo.";
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
        IsFinalizing = true;
        long? savedId = null;
        try
        {
            StatusText = "Deteniendo y guardando…";
            await transcriber.StopAsync();
            UpdateElapsed(transcriber);
            _timer.Stop();

            // Cierra los WAV antes de nada: todos los bloques ya se entregaron.
            var micPath = _micWriter?.FilePath;
            var loopPath = _loopWriter?.FilePath;
            DisposeAudioWriters();

            // La sesión se guarda sin transcripción: el audio queda en su carpeta
            // y se transcribe cuando el usuario lo decida (ahora o más tarde).
            var duration = transcriber.ActiveElapsed;
            var title = $"Llamada {_sessionStart:dd/MM HH:mm}";
            var notes = micPath is null ? string.Empty : BuildAudioNotes(micPath, loopPath);
            var id = await Task.Run(() => _store.SaveSession(title, _sessionStart, duration, string.Empty, notes, []));

            var audioFolder = AudioDirectory;
            if (micPath is not null && _store is ISessionFileStore fileStore)
            {
                string[] audioFiles = loopPath is null ? [micPath] : [micPath, loopPath];
                await Task.Run(() => fileStore.AttachFiles(id, audioFiles));
                audioFolder = fileStore.GetSessionFolder(id) ?? audioFolder;
            }

            savedId = id;
            StatusText = $"Grabación guardada en {audioFolder}.";
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
            IsFinalizing = false;
            OnPropertyChanged(nameof(CanStart));
            CommandManager.InvalidateRequerySuggested();
        }

        if (savedId is { } sessionId)
        {
            await RequestSessionTranscriptionAsync(sessionId, justRecorded: true);
        }
    }

    /// <summary>Notas de sesión con la ruta de los WAV conservados.</summary>
    private static string BuildAudioNotes(string micPath, string? loopPath) => loopPath is null
        ? $"Audio (micro): {micPath}"
        : $"Audio (micro): {micPath}\nAudio (llamada): {loopPath}";

    /// <summary>Sufijo del WAV del micrófono (tu voz).</summary>
    public const string MicSuffix = "-micro.wav";

    /// <summary>Sufijo del WAV de la llamada (la otra persona).</summary>
    public const string CallSuffix = "-llamada.wav";

    /// <summary>
    /// Busca en la carpeta de una sesión sus grabaciones (micrófono y llamada).
    /// Devuelve rutas nulas si no hay audio.
    /// </summary>
    public static (string? Mic, string? Call) FindRecordings(string? folder)
    {
        if (folder is null || !Directory.Exists(folder))
        {
            return (null, null);
        }

        var files = Directory.EnumerateFiles(folder, "*.wav").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        return (
            files.FirstOrDefault(f => f.EndsWith(MicSuffix, StringComparison.OrdinalIgnoreCase)),
            files.FirstOrDefault(f => f.EndsWith(CallSuffix, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Pregunta si transcribir (y con qué motor) la grabación de una sesión y,
    /// si se acepta, la transcribe. Sirve tanto al detener como para
    /// regenerar la transcripción desde el historial.
    /// </summary>
    public async Task RequestSessionTranscriptionAsync(long sessionId, bool justRecorded = false)
    {
        if (!CanStart || _store is not ISessionFileStore fileStore || _store.GetSession(sessionId) is not ({ } session, var existing))
        {
            return;
        }

        var (mic, call) = FindRecordings(fileStore.GetSessionFolder(sessionId));
        if (mic is null && call is null)
        {
            StatusText = $"«{session.Title}» no tiene audio grabado: no se puede transcribir de nuevo.";
            return;
        }

        var prompt = new TranscriptionPrompt(
            justRecorded ? "Grabación terminada" : "Transcribir sesión",
            justRecorded
                ? $"«{session.Title}» se ha guardado con su audio. ¿Quieres transcribirla ahora?"
                : existing.Count > 0
                    ? $"Se volverá a transcribir «{session.Title}» y se reemplazará su transcripción actual ({existing.Count} segmentos)."
                    : $"Se transcribirá «{session.Title}».",
            justRecorded ? "Más tarde" : "Cancelar",
            AudioDuration(mic, call));
        if (ChooseEngine?.Invoke(prompt) is not { } kind)
        {
            if (justRecorded)
            {
                StatusText = "Grabación guardada sin transcribir. Puedes transcribirla cuando quieras con «Transcribir…» en el historial.";
            }

            return;
        }

        await TranscribeSessionAsync(sessionId, kind);
    }

    /// <summary>Duración aproximada de las grabaciones (bytes PCM mono 16 bits a 16 kHz).</summary>
    private static TimeSpan AudioDuration(params string?[] paths) => TimeSpan.FromSeconds(paths
        .Where(p => p is not null && File.Exists(p))
        .Sum(p => Math.Max(0, new FileInfo(p!).Length - 44) / 32000.0));

    /// <summary>
    /// Transcribe (o vuelve a transcribir) el audio guardado de una sesión con
    /// el motor indicado y sustituye su transcripción.
    /// </summary>
    public async Task TranscribeSessionAsync(long sessionId, TranscriptionEngineKind kind)
    {
        if (!CanStart || _store is not ISessionFileStore fileStore)
        {
            return;
        }

        if (Interlocked.Exchange(ref _importGuard, 1) == 1)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _importCts = cancellation;
        IsImporting = true;
        ITranscriptionEngine? engine = null;
        var title = _store.GetSession(sessionId)?.Session.Title;
        var startedAt = DateTime.Now;
        var clock = new System.Diagnostics.Stopwatch();
        var (mic, call) = FindRecordings(fileStore.GetSessionFolder(sessionId));
        var audio = AudioDuration(mic, call);
        try
        {
            Segments.Clear();
            engine = await CreateEngineAsync(kind, cancellation.Token);
            startedAt = DateTime.Now;
            clock.Start();
            var segments = await TranscribeRecordingsAsync(engine, mic, call, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            clock.Stop();

            var run = BuildRun(engine, startedAt, clock.Elapsed, audio, sessionId, title, completed: true);
            await Task.Run(() => fileStore.ReplaceSegments(sessionId, segments, run));
            RecordRun(run);
            StatusText = $"Transcripción guardada: {segments.Count} segmentos · {CostFormat.Summary(run)}.";
            LoadHistory(SearchText);
            SessionImported?.Invoke(sessionId);
        }
        catch (OperationCanceledException)
        {
            RecordFailedRun(engine, startedAt, clock, audio, sessionId, title);
            StatusText = "Transcripción cancelada: la sesión conserva su transcripción anterior.";
        }
        catch (Exception ex)
        {
            RecordFailedRun(engine, startedAt, clock, audio, sessionId, title);
            StatusText = $"No se pudo transcribir: {ex.Message} El audio sigue guardado; puedes reintentarlo con «Transcribir…».";
        }
        finally
        {
            IsImporting = false;
            _importCts = null;
            DownloadPercent = -1;
            Interlocked.Exchange(ref _importGuard, 0);
            OnPropertyChanged(nameof(CanStart));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Crea el motor elegido: Whisper local (con caché) u OpenAI (con la clave guardada).</summary>
    private async Task<ITranscriptionEngine> CreateEngineAsync(TranscriptionEngineKind kind, CancellationToken cancellationToken)
    {
        if (kind == TranscriptionEngineKind.OpenAi)
        {
            await _models.EnsureVadModelAsync(cancellationToken); // El VAD sigue siendo local.
            var key = _settings?.GetOpenAiKey();
            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("Falta la clave de OpenAI: configúrala en «Transcripción…».");
            }

            return new OpenAiTranscriber(Http, key, _settings?.OpenAiModel, SelectedLanguageCode, usdPerMinute: _settings?.OpenAiUsdPerMinute);
        }

        StatusText = "Preparando modelos…";
        DownloadPercent = 0;
        return await EnsureEngineAsync(cancellationToken);
    }

    /// <summary>
    /// Datos de una transcripción para el registro de costes. Con OpenAI se
    /// cuenta el audio enviado desde <paramref name="sentBefore"/> (un mismo
    /// motor puede transcribir varios archivos); con Whisper, el coste es 0.
    /// </summary>
    private TranscriptionRun BuildRun(
        ITranscriptionEngine engine,
        DateTime startedAt,
        TimeSpan elapsed,
        TimeSpan audio,
        long? sessionId,
        string? title,
        bool completed,
        TimeSpan sentBefore = default)
    {
        if (engine is OpenAiTranscriber openAi)
        {
            var sent = openAi.AudioSent - sentBefore;
            return new TranscriptionRun(
                startedAt,
                "OpenAI",
                openAi.Model,
                elapsed,
                sent.TotalSeconds,
                sent.TotalMinutes * openAi.PricePerMinute,
                sessionId,
                title,
                completed);
        }

        var model = SelectedModel.DisplayName.Split(" · ")[0];
        return new TranscriptionRun(startedAt, "Whisper", model, elapsed, audio.TotalSeconds, 0, sessionId, title, completed);
    }

    /// <summary>Guarda la transcripción en el registro de costes (un fallo de escritura no interrumpe).</summary>
    private void RecordRun(TranscriptionRun run)
    {
        try
        {
            _costLog?.Append(run);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin registro de costes no se pierde la transcripción.
        }
    }

    /// <summary>
    /// Si una transcripción con OpenAI falla o se cancela a medias, el audio ya
    /// enviado se ha facturado igualmente: se registra como incompleta.
    /// </summary>
    private void RecordFailedRun(
        ITranscriptionEngine? engine,
        DateTime startedAt,
        System.Diagnostics.Stopwatch clock,
        TimeSpan audio,
        long? sessionId,
        string? title,
        TimeSpan sentBefore = default)
    {
        if (engine is OpenAiTranscriber openAi && openAi.AudioSent > sentBefore)
        {
            clock.Stop();
            RecordRun(BuildRun(engine, startedAt, clock.Elapsed, audio, sessionId, title, completed: false, sentBefore));
        }
    }

    /// <summary>
    /// Transcribe los WAV del micrófono y de la llamada, etiquetando cada
    /// canal con su hablante y fusionando los segmentos ordenados por tiempo.
    /// </summary>
    private async Task<IReadOnlyList<TranscriptSegment>> TranscribeRecordingsAsync(
        ITranscriptionEngine engine,
        string? micPath,
        string? loopPath,
        CancellationToken cancellationToken)
    {
        string vadError = string.Empty;
        var vadFactory = await PreloadVadsAsync(2, e => vadError = e);

        var recordings = new List<(string Path, Speaker Speaker)>();
        if (micPath is not null)
        {
            recordings.Add((micPath, Speaker.Me));
        }

        if (loopPath is not null)
        {
            recordings.Add((loopPath, Speaker.Them));
        }

        // Duraciones aproximadas (bytes PCM: mono 16 bits a 16 kHz = 32000 B/s)
        // para repartir la barra de progreso entre archivos.
        var weights = new List<double>();
        foreach (var (path, _) in recordings)
        {
            weights.Add(File.Exists(path) ? Math.Max(1, new FileInfo(path).Length - 44) / 32000.0 : 0);
        }

        var totalWeight = Math.Max(weights.Sum(), 1);
        var doneWeight = 0.0;
        var segments = new List<TranscriptSegment>();
        DownloadPercent = 0;
        for (var i = 0; i < recordings.Count; i++)
        {
            var (path, speaker) = recordings[i];
            if (weights[i] <= 0)
            {
                continue; // Archivo vacío o ausente (canal sin audio).
            }

            var channel = speaker == Speaker.Me ? "micrófono" : "llamada";
            StatusText = $"Transcribiendo audio grabado ({channel})…";
            var offset = doneWeight / totalWeight;
            var span = weights[i] / totalWeight;
            // Cada canal empieza sin el contexto del anterior.
            await engine.ResetContextAsync();
            using var source = AudioFileDecoder.Open(path);
            var progress = new Progress<double>(p => _ui.Post(_ =>
            {
                DownloadPercent = Math.Min(1, offset + span * p);
                StatusText = $"Transcribiendo audio grabado ({channel})… {p:P0}";
            }, null));

            var fileTranscriber = new FileTranscriber(engine, vadFactory, speaker);
            fileTranscriber.SegmentReady += PostSegmentToUi;
            var channelSegments = await Task.Run(
                () => fileTranscriber.TranscribeAsync(source, progress, cancellationToken), cancellationToken);
            segments.AddRange(channelSegments);
            doneWeight += weights[i];
        }

        DownloadPercent = 1;
        return segments.OrderBy(s => s.Start).ToList();
    }

    /// <summary>Extensiones de archivo (audio/vídeo) admitidas por la importación.</summary>
    public static readonly IReadOnlyList<string> MediaExtensions =
    [
        ".mp3", ".wav", ".m4a", ".mp4", ".mkv", ".mov", ".avi",
        ".wma", ".aac", ".flac", ".ogg", ".opus", ".webm",
    ];

    /// <summary>Indica si la ruta tiene una extensión de audio/vídeo admitida.</summary>
    public static bool IsMediaFile(string path) =>
        MediaExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Transcribe archivos de audio/vídeo y guarda cada uno como sesión.
    /// Si no se indican rutas, muestra el diálogo de selección.
    /// </summary>
    public async Task TranscribeFilesAsync(IReadOnlyList<string>? filePaths = null)
    {
        if (!CanStart)
        {
            return;
        }

        if (Interlocked.Exchange(ref _importGuard, 1) == 1)
        {
            return;
        }

        try
        {
            await TranscribeFilesCoreAsync(filePaths);
        }
        finally
        {
            Interlocked.Exchange(ref _importGuard, 0);
        }
    }

    private async Task TranscribeFilesCoreAsync(IReadOnlyList<string>? filePaths)
    {
        var files = filePaths;
        if (files is null)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selecciona archivos de audio o vídeo",
                Multiselect = true,
                Filter = "Audio y vídeo|*.mp3;*.wav;*.m4a;*.mp4;*.mkv;*.mov;*.avi;*.wma;*.aac;*.flac;*.ogg;*.opus;*.webm|Todos los archivos|*.*",
            };
            if (dialog.ShowDialog() == true)
            {
                files = dialog.FileNames;
            }
        }

        if (files is null || files.Count == 0)
        {
            return;
        }

        if (!CanStart)
        {
            StatusText = "No se puede importar mientras hay una sesión de grabación en curso.";
            return;
        }

        var prompt = new TranscriptionPrompt(
            "Transcribir archivos",
            files.Count == 1
                ? $"Se transcribirá «{Path.GetFileName(files[0])}» y se guardará como una sesión."
                : $"Se transcribirán {files.Count} archivos; cada uno se guardará como una sesión.",
            "Cancelar",
            TimeSpan.Zero);
        TranscriptionEngineKind kind;
        if (ChooseEngine is null)
        {
            kind = _settings?.DefaultEngine ?? TranscriptionEngineKind.Whisper;
        }
        else if (ChooseEngine(prompt) is { } chosen)
        {
            kind = chosen;
        }
        else
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _importCts = cancellation;
        IsImporting = true;
        try
        {
            var engine = await CreateEngineAsync(kind, cancellation.Token);

            var vadError = string.Empty;
            var transcriber = new FileTranscriber(engine, CreateVadFactory(e => vadError = e));
            transcriber.SegmentReady += PostSegmentToUi;

            for (var i = 0; i < files.Count && !cancellation.IsCancellationRequested; i++)
            {
                var file = files[i];
                var name = Path.GetFileName(file);
                var prefix = files.Count > 1 ? $"[{i + 1}/{files.Count}] " : string.Empty;
                var startedAt = DateTime.Now;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var sentBefore = (engine as OpenAiTranscriber)?.AudioSent ?? TimeSpan.Zero;
                var fileAudio = TimeSpan.Zero;
                try
                {
                    StatusText = $"{prefix}Transcribiendo {name}…";
                    Segments.Clear();

                    await engine.ResetContextAsync();
                    using var source = AudioFileDecoder.Open(file);
                    fileAudio = source.Duration;
                    var progress = new Progress<double>(p => _ui.Post(_ =>
                    {
                        DownloadPercent = p;
                        StatusText = $"{prefix}Transcribiendo {name}… {p:P0}";
                    }, null));
                    var segments = await Task.Run(
                        () => transcriber.TranscribeAsync(source, progress, cancellation.Token));
                    cancellation.Token.ThrowIfCancellationRequested();

                    var title = $"Archivo {Path.GetFileNameWithoutExtension(file)} {startedAt:dd/MM HH:mm}";
                    var duration = source.Duration;
                    var notes = $"Importado: {file}";
                    var savedId = await Task.Run(() => _store.SaveSession(
                        title,
                        startedAt,
                        duration,
                        "archivo",
                        notes,
                        segments));
                    clock.Stop();

                    var run = BuildRun(engine, startedAt, clock.Elapsed, fileAudio, savedId, title, completed: true, sentBefore);
                    if (_store is ISessionFileStore fileStore)
                    {
                        await Task.Run(() => fileStore.ReplaceSegments(savedId, segments, run));
                    }

                    RecordRun(run);
                    StatusText = vadError.Length == 0
                        ? $"{prefix}{name}: guardada · {segments.Count} segmentos · {CostFormat.Summary(run)}."
                        : $"{prefix}{name}: guardada · {segments.Count} segmentos · {CostFormat.Summary(run)} (VAD por energía).";
                    if (files.Count == 1)
                    {
                        SessionImported?.Invoke(savedId);
                    }
                }
                catch (OperationCanceledException)
                {
                    RecordFailedRun(engine, startedAt, clock, fileAudio, null, name, sentBefore);
                    StatusText = "Importación cancelada (no se ha guardado).";
                }
                catch (Exception ex)
                {
                    RecordFailedRun(engine, startedAt, clock, fileAudio, null, name, sentBefore);
                    StatusText = $"{prefix}Error con {name}: {ex.Message}";
                }
            }

            LoadHistory(SearchText);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Importación cancelada.";
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo transcribir: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
            _importCts = null;
            DownloadPercent = -1;
            OnPropertyChanged(nameof(CanStart));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task DownloadModelAsync()
    {
        if (!CanDownloadModel)
        {
            return;
        }

        IsBusy = true;
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
            IsBusy = false;
            DownloadPercent = -1;
            RefreshModelAvailability();
        }
    }

    public async Task StopIfActiveAsync()
    {
        if (_transcriber is not null && HasUnsavedLiveSession)
        {
            await StopSessionAsync();
        }
    }

    /// <summary>Solicita cancelar la importación en curso, si la hay.</summary>
    public void CancelImport() => _importCts?.Cancel();

    /// <summary>
    /// Libera los recursos de transcripción (capturas en curso, motor Whisper y
    /// temporizador). Idempotente; pensado para el cierre de la aplicación.
    /// </summary>
    public void DisposeResources()
    {
        _timer.Stop();
        var transcriber = Interlocked.Exchange(ref _transcriber, null);
        transcriber?.Dispose();
        DisposeAudioWriters();
        var engine = Interlocked.Exchange(ref _engine, null);
        engine?.Dispose();
        _engineKey = null;
    }

    /// <summary>Comprueba si hay actualizaciones y muestra el banner si las hay.</summary>
    public async Task CheckForUpdatesDelayedAsync()
    {
        await Task.Delay(5000);
        await CheckForUpdatesAsync(manual: false);
    }

    /// <summary>
    /// Consulta GitHub Releases. En la comprobación automática los fallos se
    /// ignoran; en la manual se informa al usuario del resultado.
    /// </summary>
    internal async Task CheckForUpdatesAsync(bool manual)
    {
        if (!_updates.IsSupported)
        {
            if (manual)
            {
                StatusText = $"Versión {AppVersion} (portable): las actualizaciones automáticas requieren instalar con Setup.";
            }

            return;
        }

        if (_checkingUpdates)
        {
            return;
        }

        _checkingUpdates = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            if (manual)
            {
                StatusText = "Buscando actualizaciones…";
            }

            var info = await _updates.CheckAsync();
            var version = info?.TargetFullRelease?.Version?.ToString();
            if (!string.IsNullOrEmpty(version))
            {
                UpdateVersion = version!;
                UpdateAvailable = true;
                StatusText = $"Actualización a v{version} disponible.";
            }
            else if (manual)
            {
                StatusText = $"Tienes la última versión disponible (v{AppVersion}).";
            }
        }
        catch
        {
            // Sin red o fuente inaccesible.
            if (manual)
            {
                StatusText = "No se pudo comprobar si hay actualizaciones. Revisa tu conexión.";
            }
        }
        finally
        {
            _checkingUpdates = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task UpdateNowAsync()
    {
        if (!CanStart)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var info = await _updates.CheckAsync();
            if (info?.TargetFullRelease is null)
            {
                UpdateAvailable = false;
                return;
            }

            StatusText = "Descargando actualización…";
            DownloadPercent = 0;
            await _updates.DownloadAsync(
                info,
                p => _ui.Post(_ => DownloadPercent = p, null),
                CancellationToken.None);
            StatusText = $"Actualización v{info.TargetFullRelease.Version} lista. Reiniciando…";
            _updates.ApplyAndRestart(info);
        }
        catch (Exception ex)
        {
            StatusText = $"No se pudo actualizar: {ex.Message}. Puedes volver a intentarlo.";
        }
        finally
        {
            DownloadPercent = -1;
            IsBusy = false;
        }
    }

    /// <summary>Publica un segmento en el panel de transcripción (hilo UI, ordenado por tiempo).</summary>
    private void PostSegmentToUi(TranscriptSegment segment)
    {
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

    private void UpdateElapsed() => UpdateElapsed(_transcriber);

    private void UpdateElapsed(ConversationTranscriber? transcriber)
    {
        if (transcriber is not null)
        {
            ElapsedText = transcriber.ActiveElapsed.ToString(@"hh\:mm\:ss");
        }
    }
}

/// <summary>Datos para preguntar si transcribir y con qué motor.</summary>
/// <param name="Title">Título del diálogo.</param>
/// <param name="Message">Explicación (qué se transcribe y si se reemplaza algo).</param>
/// <param name="DeclineText">Texto del botón para no transcribir ahora.</param>
/// <param name="AudioDuration">Duración del audio a transcribir (para estimar coste y tiempo).</param>
public sealed record TranscriptionPrompt(string Title, string Message, string DeclineText, TimeSpan AudioDuration);
