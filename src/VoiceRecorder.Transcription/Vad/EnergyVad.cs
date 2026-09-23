namespace VoiceRecorder.Transcription.Vad;

/// <summary>
/// VAD de respaldo por energía: umbral adaptativo sobre el RMS de la señal.
/// Menos preciso que Silero ante música/ruido, pero sin dependencias.
/// </summary>
public sealed class EnergyVad : IVad
{
    private const int WindowSamples = 512;
    private const float FloorRise = 0.01f;
    private const float MinFloor = 0.0005f;

    private float _noiseFloor = 0.01f;

    public int WindowSize => WindowSamples;

    public float DetectVoiceProbability(ReadOnlySpan<float> window)
    {
        float sumSquares = 0f;
        for (var i = 0; i < window.Length; i++)
        {
            sumSquares += window[i] * window[i];
        }

        var rms = MathF.Sqrt(sumSquares / MathF.Max(1, window.Length));

        if (rms < _noiseFloor)
        {
            _noiseFloor = rms;
        }
        else
        {
            _noiseFloor += (rms - _noiseFloor) * FloorRise;
        }

        _noiseFloor = MathF.Max(_noiseFloor, MinFloor);
        var ratio = rms / _noiseFloor;
        // Ratio ~1: ruido de fondo; ratio > 8: probable voz. Sigmoide suave.
        var probability = 1f / (1f + MathF.Exp(-(ratio - 8f) / 2f));
        return Math.Clamp(probability, 0f, 1f);
    }

    public void Reset()
    {
        _noiseFloor = 0.01f;
    }

    public void Dispose()
    {
    }
}
