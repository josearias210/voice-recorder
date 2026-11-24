using System.Net;
using System.Text;
using VoiceRecorder.Core;
using VoiceRecorder.Transcription;

namespace VoiceRecorder.Tests;

/// <summary>Motor de OpenAI contra un servidor simulado (sin red).</summary>
public class OpenAiTranscriberTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static Utterance Voice(Speaker speaker, double startSeconds, double seconds = 1)
    {
        var start = TimeSpan.FromSeconds(startSeconds);
        return new Utterance(speaker, new float[(int)(seconds * AudioConstants.TargetSampleRate)], start, start + TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public async Task Envia_El_Audio_Y_Asigna_Los_Tiempos_Del_Bloque()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, """{"text":"Hola, ¿qué tal?","languages":[{"code":"es"}]}"""));
        var engine = new OpenAiTranscriber(new HttpClient(handler), "sk-prueba");

        var segments = await engine.TranscribeAsync(Voice(Speaker.Them, 12, 2), CancellationToken.None);

        var segment = Assert.Single(segments);
        Assert.Equal(Speaker.Them, segment.Speaker);
        Assert.Equal(TimeSpan.FromSeconds(12), segment.Start);
        Assert.Equal(TimeSpan.FromSeconds(14), segment.End);
        Assert.Equal("Hola, ¿qué tal?", segment.Text);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.openai.com/v1/audio/transcriptions", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("sk-prueba", request.Headers.Authorization.Parameter);
        Assert.Contains("gpt-transcribe", body);
        Assert.Contains("languages[]", body);
        Assert.Contains("audio.wav", body);
        Assert.Contains("audio/wav", body);
        Assert.Equal(TimeSpan.FromSeconds(2), engine.AudioSent);
    }

    [Fact]
    public async Task El_Texto_Anterior_Del_Mismo_Hablante_Va_Como_Contexto()
    {
        var replies = new Queue<string>(["Primera frase.", "Respuesta.", "Segunda frase.", "Otra llamada."]);
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, $$"""{"text":"{{replies.Dequeue()}}"}"""));
        var engine = new OpenAiTranscriber(new HttpClient(handler), "sk-prueba");

        await engine.TranscribeAsync(Voice(Speaker.Me, 0), CancellationToken.None);
        await engine.TranscribeAsync(Voice(Speaker.Them, 2), CancellationToken.None);
        await engine.TranscribeAsync(Voice(Speaker.Me, 4), CancellationToken.None);

        Assert.DoesNotContain("prompt", handler.Requests[0].Body);
        Assert.DoesNotContain("Primera frase.", handler.Requests[1].Body); // otro hablante
        Assert.Contains("prompt", handler.Requests[2].Body);
        Assert.Contains("Primera frase.", handler.Requests[2].Body);

        await engine.ResetContextAsync();
        await engine.TranscribeAsync(Voice(Speaker.Me, 6), CancellationToken.None);
        Assert.DoesNotContain("prompt", handler.Requests[3].Body);
    }

    [Fact]
    public async Task Idioma_Automatico_No_Envia_Languages()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, """{"text":"hello"}"""));
        var engine = new OpenAiTranscriber(new HttpClient(handler), "sk-prueba", language: "auto");

        await engine.TranscribeAsync(Voice(Speaker.Me, 0), CancellationToken.None);

        Assert.DoesNotContain("languages", Assert.Single(handler.Requests).Body);
    }

    [Fact]
    public async Task Clave_Invalida_Da_Un_Error_Claro_Sin_Reintentar()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.Unauthorized, """{"error":{"message":"Incorrect API key provided"}}"""));
        var engine = new OpenAiTranscriber(new HttpClient(handler), "sk-mala");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.TranscribeAsync(Voice(Speaker.Me, 0), CancellationToken.None));

        Assert.Contains("clave de OpenAI no es válida", error.Message);
        Assert.Contains("Incorrect API key provided", error.Message);
        Assert.Single(handler.Requests);
        Assert.Equal(TimeSpan.Zero, engine.AudioSent);
    }

    [Fact]
    public async Task Limite_De_Uso_Se_Reintenta()
    {
        var calls = 0;
        var handler = new FakeHandler((_, _) => ++calls < 3
            ? Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"Rate limit"}}""")
            : Json(HttpStatusCode.OK, """{"text":"por fin"}"""));
        var engine = new OpenAiTranscriber(new HttpClient(handler), "sk-prueba") { RetryDelay = TimeSpan.FromMilliseconds(1) };

        var segments = await engine.TranscribeAsync(Voice(Speaker.Me, 0), CancellationToken.None);

        Assert.Equal("por fin", Assert.Single(segments).Text);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Respuesta_Sin_Texto_No_Crea_Segmentos()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, """{"text":" ... "}"""));
        var engine = new OpenAiTranscriber(new HttpClient(handler), "sk-prueba");

        Assert.Empty(await engine.TranscribeAsync(Voice(Speaker.Me, 0), CancellationToken.None));
    }

    [Fact]
    public async Task VerifyAsync_Consulta_El_Modelo_Sin_Enviar_Audio()
    {
        var handler = new FakeHandler((_, _) => Json(HttpStatusCode.OK, """{"id":"gpt-transcribe"}"""));
        var engine = new OpenAiTranscriber(new HttpClient(handler), "sk-prueba");

        await engine.VerifyAsync(CancellationToken.None);

        var (request, _) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.openai.com/v1/models/gpt-transcribe", request.RequestUri!.ToString());
    }

    [Fact]
    public void Sin_Clave_No_Se_Puede_Crear()
    {
        Assert.Throws<ArgumentException>(() => new OpenAiTranscriber(new HttpClient(), " "));
    }

    [Fact]
    public void WavEncoder_Genera_Cabecera_PCM_Mono_16_Bits()
    {
        var wav = WavEncoder.Encode([0f, 1f, -1f]);

        Assert.Equal(44 + 6, wav.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22)); // mono
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(6, BitConverter.ToInt32(wav, 40));
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(wav, 46));
        Assert.Equal(-short.MaxValue, BitConverter.ToInt16(wav, 48));
    }
}
