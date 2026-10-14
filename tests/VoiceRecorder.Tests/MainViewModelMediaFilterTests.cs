using VoiceRecorder.App.ViewModels;

namespace VoiceRecorder.Tests;

public class MainViewModelMediaFilterTests
{
    [Theory]
    [InlineData("entrevista.mp3", true)]
    [InlineData("VIDEO.MP4", true)]
    [InlineData("audio.WAV", true)]
    [InlineData("clase.mkv", true)]
    [InlineData("nota.txt", false)]
    [InlineData("sinextension", false)]
    [InlineData("documento.pdf", false)]
    public void IsMediaFile_Detecta_Por_Extension_Sin_Distinguir_Mayusculas(string fileName, bool expected)
    {
        var path = Path.Combine(Path.GetTempPath(), fileName);

        Assert.Equal(expected, MainViewModel.IsMediaFile(path));
    }

    [Fact]
    public void MediaExtensions_No_Tiene_Duplicados_Ni_Nulos()
    {
        Assert.All(MainViewModel.MediaExtensions, extension => Assert.StartsWith(".", extension));
        Assert.Equal(MainViewModel.MediaExtensions.Count, MainViewModel.MediaExtensions.Distinct().Count());
    }
}
