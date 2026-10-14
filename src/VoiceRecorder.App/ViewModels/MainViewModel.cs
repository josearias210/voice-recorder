using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VoiceRecorder.App.Infrastructure;
using VoiceRecorder.App.Services;
using VoiceRecorder.Audio;
using VoiceRecorder.Core;
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
    private readonly SynchronizationContext _ui;
    private readonly DispatcherTimer _timer;

    private ConversationTranscriber? _transcriber;
    private WhisperTranscriber? _engine;
    private string? _engineKey;
    private DateTime _sessionStart;
    private readonly List<TranscriptSegment> _liveSegments = [];
    private SessionState _state = SessionState.Idle;
    private int _importGuard;
    private CancellationTokenSource? _importCts;
    private bool _settingsLoaded;
    private WavChunkWriter? _micWriter;
    private WavChunkWriter? _loopWriter;
    private bool _isFinalizing;
    private bool _recordAudioFirst;

    public MainViewModel(
        ISessionStore store,
        ModelManager models,
        IAudioDeviceEnumerator devices,
        ThemeService? themes = null,
        SettingsService? settings = null)
    {
        _store = store;
        _models = models;
        _devices = devices;
        _themes = themes;
        _settings = settings;
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
        _recordAudioFirst = _settings?.RecordAudioFirst ?? false;
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
        UpdateNowCommand = new AsyncRelayCommand(_ => UpdateNowAsync(), _ => UpdateAvailable);
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
    /// Modo "grabar primero": el audio se guarda en WAV (micrófono y llamada por
    /// separado) sin transcribir en vivo; al detener se transcribe el archivo.
    /// Arranque instantáneo (no carga modelos).
    /// </summary>
    public bool RecordAudioFirst
    {
        get => _recordAudioFirst;
        set
        {
            if (Set(ref _recordAudioFirst, value))
            {
                OnPropertyChanged(nameof(CanStart));
                PersistSettings();
            }
        }
    }

    /// <summary>Carpeta donde se guardan los WAV del modo "grabar primero".</summary>
    public static string AudioDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceRecorder",
        "audio");

    /// <summary>Indica si se está transcribiendo el audio grabado al detener la sesión.</summary>
    public bool IsFinalizing
    {
        get => _isFinalizing;
        private set
        {
            if (Set(ref _isFinalizing, value))
            {
                OnPropertyChanged(nameof(CanStart));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string? SearchText { get; set; }

    public RelayCommand SearchCommand { get; }

    public AsyncRelayCommand StartCommand { get; }

    public RelayCommand PauseResumeCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public AsyncRelayCommand TranscribeFileCommand { get; }

    public RelayCommand CancelImportCommand { get; }

    public AsyncRelayCommand DownloadModelCommand { get; }

    public AsyncRelayCommand UpdateNowCommand { get; }

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
    public bool CanStart => State is SessionState.Idle or SessionState.Stopped && !IsImporting && !IsFinalizing;

    private bool CanPauseResume => State is SessionState.Recording or SessionState.Paused;

    private bool CanStop => State is SessionState.Recording or SessionState.Paused;

    private bool CanDownloadModel => !_models.IsAvailable(SelectedModel);

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
        _settings.RecordAudioFirst = RecordAudioFirst;
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
        try
        {
            WhisperTranscriber? engine = null;
            string vadError = string.Empty;
            if (RecordAudioFirst)
            {
                // Sin transcripción en vivo: no se cargan modelos, arranque inmediato.
                StatusText = "Preparando captura de audio…";
            }
            else
            {
                StatusText = "Preparando modelos…";
                DownloadPercent = 0;
                engine = await EnsureEngineAsync();
                DownloadPercent = 1;
            }

            _sessionStart = DateTime.Now;
            var vadFactory = engine is not null
                ? await PreloadVadsAsync(2, e => vadError = e)
                : CreateVadFactory(e => vadError = e);

            var microphone = new MicrophoneCaptureService(WasapiDeviceEnumerator.ResolveMicrophoneById(SelectedMicrophone?.Id));
            var loopback = new LoopbackCaptureService();
            var transcriber = new ConversationTranscriber(engine, vadFactory, microphone, loopback);
            // Se asigna ya: si algo falla después, el catch puede liberarlo
            // (Dispose también cierra las capturas).
            _transcriber = transcriber;
            transcriber.SegmentReady += OnSegmentReady;
            transcriber.StateChanged += state => _ui.Post(
                _ =>
                {
                    State = state;
                    CommandManager.InvalidateRequerySuggested();
                },
                null);
            transcriber.ErrorOccurred += ex => _ui.Post(_ => StatusText = $"Error: {ex.Message}", null);

            if (RecordAudioFirst)
            {
                var baseName = $"grabacion-{_sessionStart:yyyyMMdd-HHmmss}";
                _micWriter = new WavChunkWriter(Path.Combine(AudioDirectory, $"{baseName}-micro.wav"));
                _loopWriter = new WavChunkWriter(Path.Combine(AudioDirectory, $"{baseName}-llamada.wav"));
                transcriber.AudioTapped += OnAudioTapped;
            }

            _liveSegments.Clear();
            Segments.Clear();
            _transcriber.Start();

            _timer.Start();
            StatusText = RecordAudioFirst
                ? $"Grabando audio (micrófono y llamada por separado). Se transcribirá al detener. Carpeta: {AudioDirectory}"
                : string.IsNullOrEmpty(vadError)
                    ? "Transcripción en vivo (Silero VAD). El audio no se guarda."
                    : $"Transcripción en vivo (VAD por energía: {vadError}). El audio no se guarda.";
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

            // Cierra los WAV antes de nada: todos los bloques ya se entregaron.
            var micPath = _micWriter?.FilePath;
            var loopPath = _loopWriter?.FilePath;
            DisposeAudioWriters();

            var duration = transcriber.ActiveElapsed;
            IReadOnlyList<TranscriptSegment> segments;
            var notes = string.Empty;

            if (RecordAudioFirst && micPath is not null)
            {
                // Modo "grabar primero": ahora sí, transcripción del archivo.
                IsFinalizing = true;
                DownloadPercent = 0;
                try
                {
                    segments = await TranscribeRecordingsAsync(micPath, loopPath);
                    notes = BuildAudioNotes(micPath, loopPath);
                }
                catch (Exception ex)
                {
                    // La grabación existe aunque falle la transcripción: se guarda igual.
                    segments = [];
                    notes = BuildAudioNotes(micPath, loopPath);
                    StatusText = $"No se pudo transcribir el audio grabado ({ex.Message}); la sesión se guarda sin texto.";
                }
                finally
                {
                    IsFinalizing = false;
                    DownloadPercent = -1;
                }
            }
            else
            {
                lock (_liveSegments)
                {
                    segments = [.. _liveSegments];
                }
            }

            var title = $"Llamada {_sessionStart:dd/MM HH:mm}";
            var id = await Task.Run(() => _store.SaveSession(
                title,
                _sessionStart,
                duration,
                string.Empty,
                notes,
                segments));

            StatusText = RecordAudioFirst
                ? $"Sesión guardada ({segments.Count} segmentos). Audio conservado en {AudioDirectory}"
                : $"Sesión guardada ({segments.Count} segmentos).";
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

    /// <summary>Notas de sesión con la ruta de los WAV conservados.</summary>
    private static string BuildAudioNotes(string micPath, string? loopPath) => loopPath is null
        ? $"Audio (micro): {micPath}"
        : $"Audio (micro): {micPath}\nAudio (llamada): {loopPath}";

    /// <summary>
    /// Transcribe los WAV del micrófono y de la llamada del modo "grabar
    /// primero", etiquetando cada canal con su hablante y fusionando los
    /// segmentos ordenados por tiempo.
    /// </summary>
    private async Task<IReadOnlyList<TranscriptSegment>> TranscribeRecordingsAsync(string micPath, string? loopPath)
    {
        StatusText = "Preparando modelos…";
        var engine = await EnsureEngineAsync();

        string vadError = string.Empty;
        var vadFactory = await PreloadVadsAsync(2, e => vadError = e);

        var recordings = new List<(string Path, Speaker Speaker)> { (micPath, Speaker.Me) };
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

        var totalWeight = weights.Sum();
        var doneWeight = 0.0;
        var segments = new List<TranscriptSegment>();
        for (var i = 0; i < recordings.Count; i++)
        {
            var (path, speaker) = recordings[i];
            if (weights[i] <= 0)
            {
                continue; // Archivo vacío o ausente (canal sin audio).
            }

            StatusText = $"Transcribiendo audio grabado ({(speaker == Speaker.Me ? "micrófono" : "llamada")})…";
            var offset = doneWeight / totalWeight;
            var span = weights[i] / totalWeight;
            using var source = AudioFileDecoder.Open(path);
            var progress = new Progress<double>(p => _ui.Post(_ =>
            {
                DownloadPercent = Math.Min(1, offset + span * p);
                StatusText = $"Transcribiendo audio grabado ({(speaker == Speaker.Me ? "micrófono" : "llamada")})… {p:P0}";
            }, null));

            var fileTranscriber = new FileTranscriber(engine, vadFactory, speaker);
            var channelSegments = await Task.Run(
                () => fileTranscriber.TranscribeAsync(source, progress, CancellationToken.None));
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

        if (State is SessionState.Recording or SessionState.Paused)
        {
            StatusText = "No se puede importar mientras hay una sesión de grabación en curso.";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _importCts = cancellation;
        IsImporting = true;
        try
        {
            StatusText = "Preparando modelos…";
            DownloadPercent = 0;
            var engine = await EnsureEngineAsync(cancellation.Token);

            var vadError = string.Empty;
            var transcriber = new FileTranscriber(engine, CreateVadFactory(e => vadError = e));
            transcriber.SegmentReady += PostSegmentToUi;

            for (var i = 0; i < files.Count && !cancellation.IsCancellationRequested; i++)
            {
                var file = files[i];
                var name = Path.GetFileName(file);
                var prefix = files.Count > 1 ? $"[{i + 1}/{files.Count}] " : string.Empty;
                try
                {
                    StatusText = $"{prefix}Transcribiendo {name}…";
                    Segments.Clear();

                    var startedAt = DateTime.Now;
                    using var source = AudioFileDecoder.Open(file);
                    var progress = new Progress<double>(p => _ui.Post(_ =>
                    {
                        DownloadPercent = p;
                        StatusText = $"{prefix}Transcribiendo {name}… {p:P0}";
                    }, null));
                    var segments = await Task.Run(
                        () => transcriber.TranscribeAsync(source, progress, cancellation.Token));

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

                    StatusText = vadError.Length == 0
                        ? $"{prefix}{name}: guardada ({segments.Count} segmentos)."
                        : $"{prefix}{name}: guardada ({segments.Count} segmentos, VAD por energía).";
                    if (files.Count == 1)
                    {
                        SessionImported?.Invoke(savedId);
                    }
                }
                catch (OperationCanceledException)
                {
                    StatusText = "Importación cancelada (no se ha guardado).";
                }
                catch (Exception ex)
                {
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
        try
        {
            await Task.Delay(5000);
            if (!_updates.IsSupported)
            {
                return;
            }

            var info = await _updates.CheckAsync();
            var version = info?.TargetFullRelease?.Version?.ToString();
            if (!string.IsNullOrEmpty(version))
            {
                UpdateVersion = version!;
                UpdateAvailable = true;
                StatusText = $"Actualización a v{version} disponible.";
            }
        }
        catch
        {
            // Sin red o fuente inaccesible: se ignora silenciosamente.
        }
    }

    private async Task UpdateNowAsync()
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

    private void OnSegmentReady(TranscriptSegment segment)
    {
        lock (_liveSegments)
        {
            _liveSegments.Add(segment);
        }

        PostSegmentToUi(segment);
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

    private void UpdateElapsed()
    {
        if (_transcriber is not null)
        {
            ElapsedText = _transcriber.ActiveElapsed.ToString(@"hh\:mm\:ss");
        }
    }
}
