using System.Net;
using System.Text;
using System.Text.Json;
using VoiceRecorder.Core;
using VoiceRecorder.Transcription;

namespace VoiceRecorder.Tests;

/// <summary>Summaries with OpenAI against a fake server (no network).</summary>
public class OpenAiSummarizerTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string Completion =
        """{"choices":[{"message":{"role":"assistant","content":"## Resumen\nSe acordó el presupuesto.\n"}}],"usage":{"prompt_tokens":1000000,"completion_tokens":500000}}""";

    private static readonly TranscriptSegment[] Segments =
    [
        new(Speaker.Them, TimeSpan.FromSeconds(65), TimeSpan.FromSeconds(70), "¿Cerramos el presupuesto?"),
        new(Speaker.Me, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "Hola"),
        new(Speaker.Me, TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(81), "   "),
    ];

    [Fact]
    public async Task Envia_Solo_El_Texto_Y_Calcula_El_Coste_Por_Tokens()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, Completion));
        var summarizer = new OpenAiSummarizer(new HttpClient(handler), "sk-prueba");

        var summary = await summarizer.SummarizeAsync("Llamada", Segments, "Cliente nuevo", CancellationToken.None);

        Assert.Equal("## Resumen\nSe acordó el presupuesto.", summary.Text);
        Assert.Equal(OpenAiSummarizer.DefaultModel, summary.Model);
        // 1 M input tokens × 0.25 + 0.5 M output tokens × 2.00
        Assert.Equal(1.25, summary.CostUsd, 6);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.openai.com/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("sk-prueba", request.Headers.Authorization!.Parameter);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(OpenAiSummarizer.DefaultModel, json.RootElement.GetProperty("model").GetString());
        var user = json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.Contains("Título: Llamada", user);
        Assert.Contains("Notas del usuario: Cliente nuevo", user);
        Assert.Contains("[00:00:02] Yo: Hola", user);
        Assert.Contains("[00:01:05] Ellos: ¿Cerramos el presupuesto?", user);
        Assert.True(user.IndexOf("Hola", StringComparison.Ordinal) < user.IndexOf("¿Cerramos", StringComparison.Ordinal));
    }

    [Fact]
    public void La_Transcripcion_Ordena_Por_Tiempo_Y_Omite_Segmentos_Vacios()
    {
        var transcript = OpenAiSummarizer.BuildTranscript(Segments);

        Assert.Equal(
            "[00:00:02] Yo: Hola" + Environment.NewLine + "[00:01:05] Ellos: ¿Cerramos el presupuesto?" + Environment.NewLine,
            transcript);
    }

    [Fact]
    public async Task Sin_Transcripcion_No_Llama_A_La_Api()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, Completion));
        var summarizer = new OpenAiSummarizer(new HttpClient(handler), "sk-prueba");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => summarizer.SummarizeAsync("Vacía", [], null, CancellationToken.None));

        Assert.Contains("no tiene transcripción", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Reintenta_Ante_Limite_De_Uso_Y_Usa_El_Modelo_Y_Precios_Configurados()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls == 1
            ? Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"slow down"}}""")
            : Json(HttpStatusCode.OK, Completion));
        var summarizer = new OpenAiSummarizer(new HttpClient(handler), "sk-prueba", "otro-modelo", usdPerMillionInput: 1, usdPerMillionOutput: 4)
        {
            RetryDelay = TimeSpan.Zero,
        };

        var summary = await summarizer.SummarizeAsync("Llamada", Segments, null, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("otro-modelo", summary.Model);
        Assert.Equal(3, summary.CostUsd, 6);
    }

    [Fact]
    public async Task El_Analisis_Usa_Su_Rubrica_Y_Envia_Las_Estadisticas_De_Habla()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, Completion));
        var summarizer = new OpenAiSummarizer(new HttpClient(handler), "sk-prueba");

        await summarizer.ReviewAsync("Llamada", Segments, null, CancellationToken.None);

        using var json = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var system = json.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        var user = json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Assert.Contains("Escucha activa", system);
        Assert.Contains("## Fallas", system);
        Assert.Contains("## Mejoras", system);
        Assert.Contains("## Criterios evaluados", system);
        Assert.Contains("Estadísticas: Tiempo de habla", user);
    }

    [Fact]
    public async Task El_Resumen_No_Incluye_Estadisticas()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, Completion));
        var summarizer = new OpenAiSummarizer(new HttpClient(handler), "sk-prueba");

        await summarizer.SummarizeAsync("Llamada", Segments, null, CancellationToken.None);

        Assert.DoesNotContain("Estadísticas:", Assert.Single(handler.Requests).Body);
    }

    [Fact]
    public void Las_Estadisticas_Cuentan_Tiempo_Turnos_Y_Preguntas()
    {
        TranscriptSegment[] segments =
        [
            new(Speaker.Me, TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(10), "Hola, ¿cómo estás?"),
            new(Speaker.Me, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), "¿Vemos el plan?"),
            new(Speaker.Them, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(50), "Sí, claro."),
            new(Speaker.Me, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(55), "Perfecto."),
        ];

        var stats = OpenAiSummarizer.BuildConversationStats(segments);

        Assert.Contains("Yo 25 s", stats);
        Assert.Contains("Ellos 30 s", stats);
        Assert.Contains("Turnos: Yo 2, Ellos 1", stats);
        Assert.Contains("Preguntas formuladas por Yo: 2", stats);
        Assert.Contains("Turno más largo de Yo: 20 s", stats);
    }

    [Fact]
    public async Task Un_Error_De_Clave_Se_Explica_Sin_Reintentar()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.Unauthorized, """{"error":{"message":"Incorrect API key"}}"""));
        var summarizer = new OpenAiSummarizer(new HttpClient(handler), "sk-mala");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => summarizer.SummarizeAsync("Llamada", Segments, null, CancellationToken.None));

        Assert.Contains("clave de OpenAI no es válida", error.Message);
        Assert.Single(handler.Requests);
    }
}
