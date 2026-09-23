using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VoiceRecorder.Transcription.Vad;

/// <summary>
/// VAD Silero v5 (ONNX). Protocolo del wrapper oficial a 16kHz:
/// entrada [1, 64 contexto + 512 muestras], estado [2, 1, 128], sr int64 escalar.
/// <see cref="WindowSize"/> expone solo las muestras de voz (512); el contexto
/// se gestiona internamente.
/// </summary>
public sealed class SileroVad : IVad
{
    private const int RawWindowSamples = 512;
    private const int ContextSamples = 64;
    private const int SampleRate = 16000;

    private readonly InferenceSession _session;
    private readonly float[] _state = new float[2 * 1 * 128];
    private readonly float[] _context = new float[ContextSamples];

    private SileroVad(InferenceSession session)
    {
        _session = session;
    }

    /// <summary>Crea el VAD desde un archivo .onnx.</summary>
    public static SileroVad FromFile(string onnxPath)
    {
        InferenceSession? session = null;
        try
        {
            session = new InferenceSession(onnxPath);
            return new SileroVad(session);
        }
        catch
        {
            // Libera el pool de hilos nativo si la carga falla.
            session?.Dispose();
            throw;
        }
    }

    public int WindowSize => RawWindowSamples;

    public float DetectVoiceProbability(ReadOnlySpan<float> window)
    {
        var inputArr = new float[ContextSamples + RawWindowSamples];
        Array.Copy(_context, inputArr, ContextSamples);
        window.CopyTo(inputArr.AsSpan(ContextSamples));
        Array.Copy(window.Slice(window.Length - ContextSamples).ToArray(), _context, ContextSamples);

        var input = new DenseTensor<float>(new Memory<float>(inputArr), [1, ContextSamples + RawWindowSamples]);
        var state = new DenseTensor<float>(new Memory<float>((float[])_state.Clone()), [2, 1, 128]);
        var sr = new DenseTensor<long>(new Memory<long>([SampleRate]), []);

        using var results = _session.Run(
        [
            NamedOnnxValue.CreateFromTensor("input", input),
            NamedOnnxValue.CreateFromTensor("state", state),
            NamedOnnxValue.CreateFromTensor("sr", sr),
        ]);

        var probability = results.First(v => v.Name == "output").AsTensor<float>().ToArray()[0];
        var newState = results.First(v => v.Name == "stateN").AsTensor<float>().ToArray();
        Array.Copy(newState, _state, _state.Length);
        return probability;
    }

    public void Reset()
    {
        Array.Clear(_state);
        Array.Clear(_context);
    }

    public void Dispose() => _session.Dispose();
}
