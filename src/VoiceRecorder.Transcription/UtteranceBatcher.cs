using VoiceRecorder.Core;

namespace VoiceRecorder.Transcription;

/// <summary>
/// Agrupa enunciados consecutivos de un canal en bloques de hasta
/// <see cref="MaxBlock"/>. Whisper acierta mucho más con unos 20-30 s de
/// audio que con frases sueltas de 1-3 s: tiene contexto para nombres,
/// puntuación y palabras cortas. Los silencios largos entre frases no se
/// incluyen; <see cref="Utterance.Pieces"/> conserva los tiempos reales.
/// </summary>
public sealed class UtteranceBatcher
{
    /// <summary>Duración máxima de un bloque (la ventana de Whisper es de 30 s).</summary>
    public static readonly TimeSpan MaxBlock = TimeSpan.FromSeconds(28);

    private readonly List<Utterance> _current = [];
    private int _currentSamples;

    private static int MaxBlockSamples => (int)(MaxBlock.TotalSeconds * AudioConstants.TargetSampleRate);

    /// <summary>
    /// Añade un enunciado. Si no cabe en el bloque en curso, devuelve ese
    /// bloque cerrado y empieza uno nuevo con el enunciado.
    /// </summary>
    public Utterance? Add(Utterance utterance)
    {
        ArgumentNullException.ThrowIfNull(utterance);
        Utterance? completed = null;
        if (_current.Count > 0
            && (_currentSamples + utterance.Samples.Length > MaxBlockSamples || _current[^1].Speaker != utterance.Speaker))
        {
            completed = Flush();
        }

        _current.Add(utterance);
        _currentSamples += utterance.Samples.Length;
        return completed;
    }

    /// <summary>Cierra y devuelve el bloque en curso, o nulo si está vacío.</summary>
    public Utterance? Flush()
    {
        if (_current.Count == 0)
        {
            return null;
        }

        var block = Combine(_current);
        _current.Clear();
        _currentSamples = 0;
        return block;
    }

    /// <summary>Une los enunciados en uno solo, con un tramo por enunciado.</summary>
    public static Utterance Combine(IReadOnlyList<Utterance> utterances)
    {
        ArgumentNullException.ThrowIfNull(utterances);
        if (utterances.Count == 1)
        {
            return utterances[0];
        }

        var samples = new float[utterances.Sum(u => u.Samples.Length)];
        var pieces = new List<UtterancePiece>(utterances.Count);
        var position = 0;
        foreach (var utterance in utterances)
        {
            pieces.Add(new UtterancePiece(
                TimeSpan.FromSeconds((double)position / AudioConstants.TargetSampleRate),
                utterance.Start));
            Array.Copy(utterance.Samples, 0, samples, position, utterance.Samples.Length);
            position += utterance.Samples.Length;
        }

        return new Utterance(utterances[0].Speaker, samples, utterances[0].Start, utterances[^1].End)
        {
            Pieces = pieces,
        };
    }
}
