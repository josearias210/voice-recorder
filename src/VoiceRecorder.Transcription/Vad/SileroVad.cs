using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VoiceRecorder.Transcription.Vad;

/// <summary>
/// VAD Silero v5 (ONNX). Protocolo del wrapper oficial a 16kHz:
/// entrada [1, 64 contexto + 512 muestras], estado [2, 1, 128], sr int64 escalar.
/// <see cref="WindowSize"/> expone solo las muestras de voz (512); el contexto
/// se gestiona internamente.
/// </summary>
/// <remarks>
/// Se ejecuta ~31 veces por segundo y canal: los tensores de entrada y el buffer
/// de estado se reutilizan entre llamadas (uso mono-hilo por instancia) para
/// no presionar al GC con asignaciones por ventana.
/// </remarks>
public sealed class SileroVad : IVad
{
    private const int RawWindowSamples = 512;
    private const int ContextSamples = 64;
    private const int SampleRate = 16000;
    private const int InputSamples = ContextSamples + RawWindowSamples;
    private const string InputName = "input";
    private const string StateName = "state";
    private const string StateOutputName = "stateN";
    private const string OutputName = "output";

    private readonly InferenceSession _session;
    private readonly float[] _state = new float[2 * 1 * 128];
    private readonly float[] _context = new float[ContextSamples];
    private readonly float[] _input = new float[InputSamples];
    private readonly List<NamedOnnxValue> _bindings;

    private SileroVad(InferenceSession session)
    {
        _session = session;
        _bindings =
        [
            NamedOnnxValue.CreateFromTensor(
                InputName, new DenseTensor<float>(new Memory<float>(_input), [1, InputSamples])),
            NamedOnnxValue.CreateFromTensor(
                StateName, new DenseTensor<float>(new Memory<float>(_state), [2, 1, 128])),
            NamedOnnxValue.CreateFromTensor(
                "sr", new DenseTensor<long>(new Memory<long>([SampleRate]), [])),
        ];
    }

    /// <summary>Crea el VAD desde un archivo .onnx.</summary>
    public static SileroVad FromFile(string onnxPath)
    {
        // El modelo es minúsculo y se invoca por ventanas de 32ms: un solo hilo
        // intra-op evita el coste de sincronización del pool por cada llamada.
        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
        };

        InferenceSession? session = null;
        try
        {
            session = new InferenceSession(onnxPath, options);
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
        Array.Copy(_context, _input, ContextSamples);
        window.CopyTo(_input.AsSpan(ContextSamples));
        window.Slice(window.Length - ContextSamples).CopyTo(_context);

        float probability = 0f;
        using var results = _session.Run(_bindings);
        foreach (var value in results)
        {
            if (value.Name == OutputName)
            {
                probability = value.AsTensor<float>().ToArray()[0];
            }
            else if (value.Name == StateOutputName)
            {
                var newState = value.AsTensor<float>().ToArray();
                Array.Copy(newState, _state, _state.Length);
            }
        }

        return probability;
    }

    public void Reset()
    {
        Array.Clear(_state);
        Array.Clear(_context);
    }

    public void Dispose() => _session.Dispose();
}
