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
    private readonly string _modelsDir = Path.Combine(Path.GetTempPath(), $"vr-models-{Guid.NewGuid():N}");
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"vr-settings-{Guid.NewGuid():N}");
    private readonly MainViewModel _viewModel;

    public MainViewModelOptionsTests()
    {
        _viewModel = new MainViewModel(new EmptyStore(), new ModelManager(_modelsDir), new EmptyDevices());
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _modelsDir, _dataDir })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
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
    public void Cambiar_RecordAudioFirst_Persiste_La_Preferencia()
    {
        var settings = new SettingsService(_dataDir);
        var viewModel = new MainViewModel(new EmptyStore(), new ModelManager(_modelsDir), new EmptyDevices(), settings: settings);

        Assert.False(viewModel.RecordAudioFirst);
        viewModel.RecordAudioFirst = true;

        var reloaded = new SettingsService(_dataDir);
        reloaded.Load();
        Assert.True(reloaded.RecordAudioFirst);
    }

    [Fact]
    public void RecordAudioFirst_Guardado_Se_Restaura_Al_Arrancar()
    {
        var settings = new SettingsService(_dataDir) { RecordAudioFirst = true };
        settings.Save();

        var viewModel = new MainViewModel(new EmptyStore(), new ModelManager(_modelsDir), new EmptyDevices(), settings: settings);

        Assert.True(viewModel.RecordAudioFirst);
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
