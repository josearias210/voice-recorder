using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VoiceRecorder.Core;

namespace VoiceRecorder.Transcription;

/// <summary>
/// Generates a summary of a transcript with the OpenAI API (<c>POST /v1/chat/completions</c>).
/// Only the transcript text is sent (never the audio), and only when the user asks for it
/// or turns on automatic summaries.
/// </summary>
public sealed class OpenAiSummarizer
{
    public const string DefaultModel = "gpt-5-mini";

    /// <summary>Reference price of <see cref="DefaultModel"/>: USD per million input tokens.</summary>
    public const double DefaultUsdPerMillionInput = 0.25;

    /// <summary>Reference price of <see cref="DefaultModel"/>: USD per million output tokens.</summary>
    public const double DefaultUsdPerMillionOutput = 2.00;

    /// <summary>Upper bound on the transcript sent, to stay well inside the model's context.</summary>
    public const int MaxTranscriptChars = 400_000;

    private const int MaxAttempts = 3;

    private const string Instructions =
        """
        Eres un asistente que resume conversaciones transcritas automáticamente.
        «Yo» es la persona que grabó la llamada y «Ellos» son los demás participantes.
        La transcripción puede tener errores: corrígelos por contexto sin inventar nada.
        Escribe en el mismo idioma que la conversación, en Markdown y solo con estas secciones
        (omite las que queden vacías):

        ## Resumen
        Dos a cinco frases con el propósito y el resultado de la conversación.

        ## Puntos clave
        - Un punto por línea.

        ## Decisiones
        - Lo que se acordó.

        ## Tareas
        - [ ] Tarea concreta (responsable: Yo / Ellos / nombre si se menciona; fecha si se dice).

        ## Preguntas abiertas
        - Lo que quedó pendiente de resolver.

        No añadas introducciones, conclusiones ni texto fuera de esas secciones.
        """;

    /// <summary>
    /// Rubric of the experimental communication review. The criteria restate, in our own words,
    /// classic principles for dealing with people (rapport, listening, handling disagreement,
    /// persuading without imposing, giving feedback); the source is deliberately not named in
    /// the UI. Only the user's own side («Yo») is evaluated.
    /// </summary>
    internal const string ReviewInstructions =
        """
        Eres un coach de comunicación interpersonal. Evalúa cómo condujo la conversación «Yo»
        (la persona que grabó la llamada); «Ellos» son los demás participantes. No evalúes a «Ellos».
        La transcripción es automática y puede tener errores: no penalices fallos de transcripción.
        Usa las estadísticas de habla como apoyo para juzgar la escucha.

        Criterios (puntúa cada uno de 1 a 5, o «sin datos» si la llamada no permite juzgarlo):
        1. Escucha activa: deja hablar, no interrumpe, hace preguntas abiertas, resume o parafrasea lo que oye.
        2. Interés genuino por la otra persona: se interesa por sus necesidades, objetivos y situación; usa su nombre.
        3. Reconocimiento sincero: valora aportes y logros concretos sin adular.
        4. Tono y cordialidad: empieza de forma amistosa, mantiene un trato cálido y positivo, evita quejas y reproches.
        5. Manejo del desacuerdo: evita discutir por ganar, no dice «estás equivocado», respeta opiniones y admite pronto sus propios errores.
        6. Empatía y perspectiva: intenta ver el asunto desde el punto de vista del otro y lo reconoce en voz alta.
        7. Persuasión colaborativa: busca acuerdos parciales, pregunta en lugar de imponer, deja que el otro haga suya la idea y la plantea en términos del beneficio del otro.
        8. Peticiones y feedback: señala problemas de forma indirecta y respetuosa, permite salvar la cara, anima y destaca lo que se puede mejorar como alcanzable.

        Escribe en el mismo idioma que la conversación, en Markdown, siempre con esta estructura exacta:

        ## Valoración general
        Puntuación global de 1 a 10 y dos o tres frases que la justifiquen.

        ## Criterios evaluados
        - **Nombre del criterio** — N/5: evidencia concreta con su marca de tiempo [hh:mm:ss].

        ## Aciertos
        - Lo que hizo bien, con su marca de tiempo y el criterio que cumple.

        ## Fallas
        - Momento concreto [hh:mm:ss], qué pasó, qué criterio incumple y por qué perjudica la conversación.

        ## Mejoras
        - Acción concreta y una frase alternativa que podría haber dicho («En lugar de …, prueba: …»).

        ## Para la próxima llamada
        - Tres prioridades como máximo.

        Sé honesto, específico y constructivo. Cita solo lo que aparece en la transcripción; no inventes momentos.
        No menciones libros, autores ni la fuente de los criterios.
        """;

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly Uri _endpoint;

    public OpenAiSummarizer(
        HttpClient http,
        string apiKey,
        string? model = null,
        Uri? endpoint = null,
        double? usdPerMillionInput = null,
        double? usdPerMillionOutput = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Falta la clave de la API de OpenAI. Configúrala en «Transcripción…».", nameof(apiKey));
        }

        _http = http;
        _apiKey = apiKey.Trim();
        _endpoint = endpoint ?? OpenAiTranscriber.DefaultEndpoint;
        Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        UsdPerMillionInput = usdPerMillionInput is > 0 ? usdPerMillionInput.Value : DefaultUsdPerMillionInput;
        UsdPerMillionOutput = usdPerMillionOutput is > 0 ? usdPerMillionOutput.Value : DefaultUsdPerMillionOutput;
    }

    public string Model { get; }

    public double UsdPerMillionInput { get; }

    public double UsdPerMillionOutput { get; }

    /// <summary>Base delay between retries (configurable in tests).</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Summarizes the transcript. Throws <see cref="InvalidOperationException"/> with a
    /// user-readable reason if there is nothing to summarize or the API fails.
    /// </summary>
    public Task<CallSummary> SummarizeAsync(
        string title,
        IReadOnlyList<TranscriptSegment> segments,
        string? notes,
        CancellationToken cancellationToken) =>
        CompleteAsync(Instructions, title, segments, notes, includeStats: false, "el resumen", cancellationToken);

    /// <summary>
    /// Experimental: reviews how «Yo» led the conversation against interpersonal-communication
    /// criteria (<see cref="ReviewInstructions"/>), with a fixed structure of strengths, failures
    /// and improvements. Throws <see cref="InvalidOperationException"/> with a readable reason.
    /// </summary>
    public Task<CallSummary> ReviewAsync(
        string title,
        IReadOnlyList<TranscriptSegment> segments,
        string? notes,
        CancellationToken cancellationToken) =>
        CompleteAsync(ReviewInstructions, title, segments, notes, includeStats: true, "el análisis", cancellationToken);

    /// <summary>
    /// Speaking statistics computed locally (they help to judge listening): speaking time and
    /// share per speaker, turns, questions asked by «Yo» and «Yo»'s longest uninterrupted turn.
    /// </summary>
    public static string BuildConversationStats(IReadOnlyList<TranscriptSegment> segments)
    {
        var ordered = segments.Where(s => !string.IsNullOrWhiteSpace(s.Text)).OrderBy(s => s.Start).ToList();
        double Seconds(Speaker speaker) => ordered.Where(s => s.Speaker == speaker).Sum(s => Math.Max(0, (s.End - s.Start).TotalSeconds));
        var me = Seconds(Speaker.Me);
        var them = Seconds(Speaker.Them);
        var total = me + them;

        // Turns: consecutive segments of the same speaker count as one turn.
        var turns = new List<(Speaker Speaker, double Seconds)>();
        foreach (var segment in ordered)
        {
            var length = Math.Max(0, (segment.End - segment.Start).TotalSeconds);
            if (turns.Count > 0 && turns[^1].Speaker == segment.Speaker)
            {
                turns[^1] = (segment.Speaker, turns[^1].Seconds + length);
            }
            else
            {
                turns.Add((segment.Speaker, length));
            }
        }

        var questions = ordered.Where(s => s.Speaker == Speaker.Me).Sum(s => s.Text.Count(c => c == '?'));
        var longestMine = turns.Where(t => t.Speaker == Speaker.Me).Select(t => t.Seconds).DefaultIfEmpty(0).Max();
        var culture = CultureInfo.GetCultureInfo("es-ES");
        return string.Format(
            culture,
            "Tiempo de habla: Yo {0:F0} s ({1:P0}) · Ellos {2:F0} s ({3:P0}). Turnos: Yo {4}, Ellos {5}. "
            + "Preguntas formuladas por Yo: {6}. Turno más largo de Yo: {7:F0} s.",
            me,
            total > 0 ? me / total : 0,
            them,
            total > 0 ? them / total : 0,
            turns.Count(t => t.Speaker == Speaker.Me),
            turns.Count(t => t.Speaker == Speaker.Them),
            questions,
            longestMine);
    }

    private async Task<CallSummary> CompleteAsync(
        string instructions,
        string title,
        IReadOnlyList<TranscriptSegment> segments,
        string? notes,
        bool includeStats,
        string what,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var transcript = BuildTranscript(segments);
        if (transcript.Length == 0)
        {
            throw new InvalidOperationException($"La sesión no tiene transcripción: transcríbela antes de generar {what}.");
        }

        if (transcript.Length > MaxTranscriptChars)
        {
            throw new InvalidOperationException($"La transcripción es demasiado larga para generar {what} de una vez.");
        }

        var input = new StringBuilder();
        input.Append("Título: ").AppendLine(title);
        if (!string.IsNullOrWhiteSpace(notes))
        {
            input.Append("Notas del usuario: ").AppendLine(notes.Trim());
        }

        if (includeStats)
        {
            input.Append("Estadísticas: ").AppendLine(BuildConversationStats(segments));
        }

        input.AppendLine().AppendLine("Transcripción:").Append(transcript);

        var body = JsonSerializer.Serialize(new
        {
            model = Model,
            messages = new object[]
            {
                new { role = "system", content = instructions },
                new { role = "user", content = input.ToString() },
            },
        });

        using var response = await SendWithRetriesAsync(body, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var (text, inputTokens, outputTokens) = ParseCompletion(json.RootElement);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"OpenAI no devolvió {what}.");
        }

        var cost = (inputTokens * UsdPerMillionInput + outputTokens * UsdPerMillionOutput) / 1_000_000;
        return new CallSummary(text.Trim(), DateTime.Now, Model, cost);
    }

    /// <summary>Transcript as one line per segment: "[00:01:05] Ellos: text".</summary>
    public static string BuildTranscript(IReadOnlyList<TranscriptSegment> segments)
    {
        var sb = new StringBuilder();
        foreach (var segment in segments.OrderBy(s => s.Start))
        {
            if (string.IsNullOrWhiteSpace(segment.Text))
            {
                continue;
            }

            sb.Append('[')
              .Append(segment.Start.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture))
              .Append("] ")
              .Append(SpeakerLabel(segment.Speaker))
              .Append(": ")
              .AppendLine(segment.Text.Trim());
        }

        return sb.ToString();
    }

    /// <summary>Spanish label of a speaker, as shown in the chat.</summary>
    public static string SpeakerLabel(Speaker speaker) => speaker switch
    {
        Speaker.Me => "Yo",
        Speaker.Them => "Ellos",
        Speaker.Both => "Ambos",
        _ => "Audio",
    };

    private static (string? Text, long InputTokens, long OutputTokens) ParseCompletion(JsonElement root)
    {
        string? text = null;
        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
        {
            text = content.GetString();
        }

        long inputTokens = 0;
        long outputTokens = 0;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("prompt_tokens", out var prompt) && prompt.TryGetInt64(out var p))
            {
                inputTokens = p;
            }

            if (usage.TryGetProperty("completion_tokens", out var completion) && completion.TryGetInt64(out var c))
            {
                outputTokens = c;
            }
        }

        return (text, inputTokens, outputTokens);
    }

    private async Task<HttpResponseMessage> SendWithRetriesAsync(string body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_endpoint, "chat/completions"))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            using (response)
            {
                var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!retryable || attempt >= MaxAttempts)
                {
                    throw new InvalidOperationException(await OpenAiTranscriber.DescribeErrorAsync(response, cancellationToken));
                }

                var delay = response.Headers.RetryAfter?.Delta ?? RetryDelay * attempt;
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
