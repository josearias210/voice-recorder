using System.Security.Cryptography;
using System.Text;
using VoiceRecorder.Transcription;

namespace VoiceRecorder.Tests;

public class ModelManagerTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), $"vr-mm-{Guid.NewGuid():N}");

    public ModelManagerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Silero_Esta_Fijado_A_Una_Etiqueta_Y_Tiene_Hash()
    {
        Assert.DoesNotContain("/master/", ModelManager.SileroVadOnnxUrl);
        Assert.Contains("/v5.", ModelManager.SileroVadOnnxUrl);
        Assert.Matches("^[0-9a-f]{64}$", ModelManager.SileroVadSha256);
    }

    [Fact]
    public async Task Descarga_Con_Hash_Correcto_Se_Guarda()
    {
        var data = Encoding.UTF8.GetBytes("modelo de prueba");
        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var destination = Path.Join(_dir, "ok.onnx");

        await using var source = new MemoryStream(data);

        await ModelManager.DownloadToFileAsync(source, destination, null, CancellationToken.None, hash);

        Assert.Equal(data, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task Descarga_Con_Hash_Incorrecto_Se_Descarta()
    {
        var destination = Path.Join(_dir, "bad.onnx");

        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("contenido manipulado"));

        await Assert.ThrowsAsync<InvalidDataException>(() => ModelManager.DownloadToFileAsync(
            source,
            destination,
            null,
            CancellationToken.None,
            new string('0', 64)));

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".download"));
    }
}
