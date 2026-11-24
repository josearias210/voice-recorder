using VoiceRecorder.App.Services;
using VoiceRecorder.App.ViewModels;
using VoiceRecorder.Core;
using VoiceRecorder.Transcription;

namespace VoiceRecorder.Tests;

/// <summary>
/// Garantiza que las colecciones enlazadas por WPF (DisplayMemberPath /
/// SelectedValuePath) expongan propiedades reales y valores por defecto coherentes.
/// </summary>
public class MainViewModelOptionsTests : IDisposable
{
    private readonly string _modelsDir = Path.Join(Path.GetTempPath(), $"vr-models-{Guid.NewGuid():N}");
    private readonly string _dataDir = Path.Join(Path.GetTempPath(), $"vr-settings-{Guid.NewGuid():N}");
    private readonly MainViewModel _viewModel;

    public MainViewModelOptionsTests()
    {
        _viewModel = new MainViewModel(new EmptyStore(), new ModelManager(_modelsDir), new EmptyDevices());
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _modelsDir, _dataDir }.Where(Directory.Exists))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LanguageOptions_Expone_Codigo_Y_Nombre_Resolvibles_Por_Wpf()
    {
        Assert.Equal(new[] { "es", "en", "auto" }, _viewModel.LanguageOptions.Select(l => l.Code));
        Assert.All(_viewModel.LanguageOptions, l => Assert.False(string.IsNullOrWhiteSpace(l.Name)));
        Assert.Equal("es", _viewModel.SelectedLanguageCode);
    }

    [Fact]
    public void ModelOptions_Tienen_DisplayName_Y_Modelo_Por_Defecto()
    {
        Assert.NotEmpty(_viewModel.ModelOptions);
        Assert.All(_viewModel.ModelOptions, m => Assert.False(string.IsNullOrWhiteSpace(m.DisplayName)));
        Assert.Equal(
            _viewModel.ModelOptions.First(m => m.FileName == "ggml-base-q5_1.bin"),
            _viewModel.SelectedModel);
    }

    [Fact]
    public void CancelImport_Y_DisposeResources_Son_Seguros_E_Idempotentes()
    {
        // Sin sesión ni importación activas no deben lanzar; y repetidos tampoco.
        _viewModel.CancelImport();
        _viewModel.DisposeResources();
        _viewModel.DisposeResources();

        Assert.False(_viewModel.IsImporting);
        Assert.Equal(AppTheme.Dark, _viewModel.SelectedTheme);
    }

    [Fact]
    public void Muestra_La_Version_De_La_App()
    {
        Assert.Equal(AppInfo.Version, _viewModel.AppVersion);
        Assert.Equal($"v{AppInfo.Version} · portable", _viewModel.VersionText);
        Assert.Contains(AppInfo.Version, _viewModel.WindowTitle);
    }

    [Fact]
    public async Task Buscar_Actualizaciones_Sin_Instalacion_Explica_Que_Requiere_Setup()
    {
        await _viewModel.CheckForUpdatesAsync(manual: true);

        Assert.Contains("Setup", _viewModel.StatusText);
        Assert.False(_viewModel.UpdateAvailable);
    }

    [Fact]
    public void Preferencias_Guardadas_Se_Restauran_Al_Arrancar()
    {
        var settings = new SettingsService(_dataDir)
        {
            ModelFileName = "ggml-small-q5_1.bin",
            LanguageCode = "en",
            MicrophoneId = "mic-usb",
        };
        settings.Save();

        var devices = new EmptyDevices(new AudioDeviceInfo("mic-default", "Micrófono por defecto", AudioDeviceKind.Microphone),
                                       new AudioDeviceInfo("mic-usb", "USB Audio", AudioDeviceKind.Microphone));
        var viewModel = new MainViewModel(new EmptyStore(), new ModelManager(_modelsDir), devices, settings: settings);

        Assert.Equal("ggml-small-q5_1.bin", viewModel.SelectedModel.FileName);
        Assert.Equal("en", viewModel.SelectedLanguageCode);
        Assert.Equal("mic-usb", viewModel.SelectedMicrophone?.Id);
    }

    [Fact]
    public void Cambiar_Idioma_O_Modelo_Persiste_Las_Preferencias()
    {
        var settings = new SettingsService(_dataDir);
        var viewModel = new MainViewModel(new EmptyStore(), new ModelManager(_modelsDir), new EmptyDevices(), settings: settings);

        viewModel.SelectedLanguageCode = "auto";
        viewModel.SelectedModel = viewModel.ModelOptions.First(m => m.FileName == "ggml-tiny-q5_1.bin");

        var reloaded = new SettingsService(_dataDir);
        reloaded.Load();
        Assert.Equal("auto", reloaded.LanguageCode);
        Assert.Equal("ggml-tiny-q5_1.bin", reloaded.ModelFileName);
    }

    [Fact]
    public void Sin_Microfono_Avisa_En_La_Pantalla_Principal()
    {
        Assert.True(_viewModel.HasSetupHint);
        Assert.Contains("micrófono", _viewModel.SetupHint);
        Assert.StartsWith("Sin micrófono · ", _viewModel.ConfigSummary);
    }

    [Fact]
    public void Con_Microfono_Avisa_Si_Falta_El_Modelo_Y_Resume_La_Configuracion()
    {
        var devices = new EmptyDevices(new AudioDeviceInfo("mic-usb", "USB Audio", AudioDeviceKind.Microphone));
        var viewModel = new MainViewModel(new EmptyStore(), new ModelManager(_modelsDir), devices);

        Assert.True(viewModel.HasSetupHint);
        Assert.Contains(viewModel.SelectedModel.DisplayName, viewModel.SetupHint);
        Assert.Equal($"USB Audio · {viewModel.SelectedModel.DisplayName} · Español · Whisper", viewModel.ConfigSummary);

        viewModel.SelectedLanguageCode = "en";
        Assert.Contains("· English ·", viewModel.ConfigSummary);
    }

    [Fact]
    public void Con_OpenAi_Por_Defecto_No_Avisa_Del_Modelo_Local()
    {
        var settings = new SettingsService(_dataDir) { DefaultEngine = TranscriptionEngineKind.OpenAi };
        var devices = new EmptyDevices(new AudioDeviceInfo("mic-usb", "USB Audio", AudioDeviceKind.Microphone));
        var viewModel = new MainViewModel(new EmptyStore(), new ModelManager(_modelsDir), devices, settings: settings);

        Assert.False(viewModel.HasSetupHint);
        Assert.EndsWith("· OpenAI", viewModel.ConfigSummary);
    }

    [Fact]
    public void Con_Microfono_Y_Modelo_Descargado_No_Hay_Aviso()
    {
        var devices = new EmptyDevices(new AudioDeviceInfo("mic-usb", "USB Audio", AudioDeviceKind.Microphone));
        var models = new ModelManager(_modelsDir);
        var viewModel = new MainViewModel(new EmptyStore(), models, devices);
        Directory.CreateDirectory(_modelsDir);
        File.WriteAllBytes(models.GetPath(viewModel.SelectedModel), [0]);

        viewModel.RefreshModelAvailability();

        Assert.False(viewModel.HasSetupHint);
        Assert.Null(viewModel.SetupHint);
    }

    private sealed class EmptyStore : ISessionStore
    {
        public long SaveSession(string title, DateTime startedAt, TimeSpan activeDuration, string tags, string notes, IReadOnlyList<TranscriptSegment> segments) => 1;

        public IReadOnlyList<SessionSummary> ListSessions(string? searchText = null) => [];

        public (SessionSummary Session, IReadOnlyList<TranscriptSegment> Segments)? GetSession(long id) => null;

        public void UpdateSession(long id, string title, string tags, string notes)
        {
        }

        public void DeleteSession(long id)
        {
        }
    }

    private sealed class EmptyDevices(params AudioDeviceInfo[] devices) : IAudioDeviceEnumerator
    {
        public IReadOnlyList<AudioDeviceInfo> ListOutputs() => [];

        public IReadOnlyList<AudioDeviceInfo> ListMicrophones() => devices;
    }
}
