using VoiceRecorder.App.ViewModels;

namespace VoiceRecorder.Tests;

/// <summary>Localización de las grabaciones de una sesión para transcribirlas (o regenerarlas).</summary>
public sealed class MainViewModelRecordingsTests : IDisposable
{
    private readonly string _folder = Path.Join(Path.GetTempPath(), $"vr-rec-{Guid.NewGuid():N}");

    public MainViewModelRecordingsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void FindRecordings_Encuentra_Micro_Y_Llamada()
    {
        File.WriteAllBytes(Path.Join(_folder, "grabacion-20261123-100000-micro.wav"), []);
        File.WriteAllBytes(Path.Join(_folder, "grabacion-20261123-100000-llamada.wav"), []);
        File.WriteAllText(Path.Join(_folder, "session.json"), "{}");

        var (mic, call) = MainViewModel.FindRecordings(_folder);

        Assert.EndsWith(MainViewModel.MicSuffix, mic);
        Assert.EndsWith(MainViewModel.CallSuffix, call);
    }

    [Fact]
    public void FindRecordings_Sin_Audio_Devuelve_Nulos()
    {
        Assert.Equal((null, null), MainViewModel.FindRecordings(_folder));
        Assert.Equal((null, null), MainViewModel.FindRecordings(null));
        Assert.Equal((null, null), MainViewModel.FindRecordings(Path.Join(_folder, "no-existe")));
    }
}
