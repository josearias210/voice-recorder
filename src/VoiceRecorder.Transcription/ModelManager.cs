using Whisper.net.Ggml;

namespace VoiceRecorder.Transcription;

/// <summary>Modelo Whisper seleccionable en la UI.</summary>
/// <param name="FileName">Nombre del archivo en caché.</param>
/// <param name="DisplayName">Etiqueta para la UI.</param>
/// <param name="Type">Variante GGML.</param>
/// <param name="Quantization">Cuantización (tamaño/velocidad).</param>
public sealed record WhisperModelInfo(
    string FileName,
    string DisplayName,
    GgmlType Type,
    QuantizationType Quantization);

/// <summary>
/// Descarga y cachea los modelos (Whisper GGML y Silero VAD) en
/// %LOCALAPPDATA%\VoiceRecorder\models.
/// </summary>
public sealed class ModelManager
{
    public static readonly IReadOnlyList<WhisperModelInfo> Catalog =
    [
        new("ggml-tiny-q5_1.bin", "Tiny · el más rápido", GgmlType.Tiny, QuantizationType.Q5_1),
        new("ggml-base-q5_1.bin", "Base · rápido (por defecto)", GgmlType.Base, QuantizationType.Q5_1),
        new("ggml-base.bin", "Base · sin cuantizar", GgmlType.Base, QuantizationType.NoQuantization),
        new("ggml-small-q5_1.bin", "Small · más preciso", GgmlType.Small, QuantizationType.Q5_1),
        // ~574 MB. Mucho más preciso en español; pensado para transcribir lo
        // grabado al detener (en directo, en CPU, se quedaría atrás).
        new("ggml-large-v3-turbo-q5_0.bin", "Large v3 Turbo · máxima precisión (574 MB)", GgmlType.LargeV3Turbo, QuantizationType.Q5_0),
    ];

    private readonly string _modelsDir;

    /// <summary>
    /// Silero VAD v5 fijado a una etiqueta (no a "master", que cambia de modelo)
    /// y verificado por SHA256 para garantizar la integridad de la descarga.
    /// </summary>
    internal const string SileroVadOnnxUrl =
        "https://github.com/snakers4/silero-vad/raw/v5.1.2/src/silero_vad/data/silero_vad.onnx";

    internal const string SileroVadSha256 = "2623a2953f6ff3d2c1e61740c6cdb7168133479b267dfef114a4a3cc5bdd788f";

    public ModelManager(string? modelsDir = null)
    {
        _modelsDir = modelsDir ?? DefaultModelsDirectory;
        Directory.CreateDirectory(_modelsDir);
    }

    public static string DefaultModelsDirectory => Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceRecorder",
        "models");

    public string VadModelPath => Path.Join(_modelsDir, "silero-vad-v5.onnx");

    public string GetPath(WhisperModelInfo model) => Path.Join(_modelsDir, model.FileName);

    public bool IsAvailable(WhisperModelInfo model) => File.Exists(GetPath(model));

    /// <summary>Devuelve la ruta del modelo, descargándolo si falta. Reporta progreso [0..1].</summary>
    public async Task<string> EnsureWhisperModelAsync(
        WhisperModelInfo model,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var path = GetPath(model);
        if (File.Exists(path))
        {
            progress?.Report(1);
            return path;
        }

        await DownloadToFileAsync(
            await WhisperGgmlDownloader.Default.GetGgmlModelAsync(model.Type, model.Quantization, cancellationToken),
            path,
            progress,
            cancellationToken);
        return path;
    }

    /// <summary>Devuelve la ruta del modelo Silero VAD (ONNX oficial), descargándolo si falta.</summary>
    public async Task<string> EnsureVadModelAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(VadModelPath))
        {
            return VadModelPath;
        }

        using var http = new HttpClient();
        await using var stream = await http.GetStreamAsync(SileroVadOnnxUrl, cancellationToken);
        await DownloadToFileAsync(stream, VadModelPath, progress: null, cancellationToken, SileroVadSha256);
        return VadModelPath;
    }

    /// <summary>
    /// Copia <paramref name="source"/> a <paramref name="destination"/> mediante un
    /// archivo temporal. Si se indica <paramref name="expectedSha256"/> y no coincide,
    /// borra la descarga y lanza <see cref="InvalidDataException"/>.
    /// </summary>
    internal static async Task DownloadToFileAsync(
        Stream source,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        string? expectedSha256 = null)
    {
        var tempPath = destination + ".download";
        await using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long total = source.CanSeek ? source.Length : -1;
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                if (total > 0)
                {
                    progress?.Report((double)written / total);
                }
            }

            if (total > 0)
            {
                progress?.Report(1);
            }
        }

        if (expectedSha256 is not null)
        {
            string actual;
            await using (var downloaded = File.OpenRead(tempPath))
            {
                actual = Convert.ToHexString(
                    await System.Security.Cryptography.SHA256.HashDataAsync(downloaded, cancellationToken));
            }

            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempPath);
                throw new InvalidDataException(
                    $"La descarga de {Path.GetFileName(destination)} no supera la verificación de integridad (SHA256).");
            }
        }

        File.Move(tempPath, destination, overwrite: true);
    }
}
