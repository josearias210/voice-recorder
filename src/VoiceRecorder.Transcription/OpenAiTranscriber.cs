using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VoiceRecorder.Core;

namespace VoiceRecorder.Transcription;

/// <summary>
/// Motor que transcribe con la API de OpenAI (<c>POST /v1/audio/transcriptions</c>).
/// <para>
/// <c>gpt-transcribe</c> devuelve solo texto, sin marcas de tiempo: por eso se
/// envía cada bloque de frases (<see cref="UtteranceBatcher"/>, hasta 28 s)
/// por separado y se le asignan los tiempos que ya conoce el VAD. Así cada
/// canal conserva su hablante y nunca se supera el límite de 25 MB por archivo.
/// </para>
/// Solo se usa cuando el usuario lo elige: envía el audio a los servidores de OpenAI.
/// </summary>
public sealed class OpenAiTranscriber : ITranscriptionEngine
{
    public const string DefaultModel = "gpt-transcribe";

    /// <summary>Precio orientativo de gpt-transcribe (USD por minuto de audio, noviembre de 2026).</summary>
    public const double UsdPerMinute = 0.0045;

    public static readonly Uri DefaultEndpoint = new("https://api.openai.com/v1/");

    /// <summary>Caracteres del texto anterior del mismo hablante que se envían como contexto.</summary>
    private const int ContextChars = 400;

    private const int MaxAttempts = 3;

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly Dictionary<Speaker, string> _context = [];
    private long _samplesSent;

    public OpenAiTranscriber(
        HttpClient http,
        string apiKey,
        string? model = null,
        string language = "es",
        Uri? endpoint = null,
        double? usdPerMinute = null)
    {
        PricePerMinute = usdPerMinute is > 0 ? usdPerMinute.Value : UsdPerMinute;
        ArgumentNullException.ThrowIfNull(http);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Falta la clave de la API de OpenAI. Configúrala en «Transcripción…».", nameof(apiKey));
        }

        _http = http;
        _apiKey = apiKey.Trim();
        Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        Language = language;
        _endpoint = endpoint ?? DefaultEndpoint;
    }

    public string Model { get; }

    /// <summary>Idioma ("es", "en"...) o "auto" para que lo detecte el servicio.</summary>
    public string Language { get; }

    /// <summary>Retardo base entre reintentos (configurable en pruebas).</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Audio enviado hasta ahora (para estimar el coste).</summary>
    public TimeSpan AudioSent => TimeSpan.FromSeconds((double)Interlocked.Read(ref _samplesSent) / AudioConstants.TargetSampleRate);

    /// <summary>Precio usado para estimar el coste (USD por minuto de audio enviado).</summary>
    public double PricePerMinute { get; }

    /// <summary>Coste estimado en USD del audio enviado.</summary>
    public double EstimatedCostUsd => AudioSent.TotalMinutes * PricePerMinute;

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(Utterance utterance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(utterance);
        if (utterance.Samples.Length == 0)
        {
            return [];
        }

        var wav = WavEncoder.Encode(utterance.Samples);
        _context.TryGetValue(utterance.Speaker, out var previous);
        var text = await SendWithRetriesAsync(wav, previous, cancellationToken);
        Interlocked.Add(ref _samplesSent, utterance.Samples.Length);

        text = WhisperTranscriber.CleanText(text);
        if (!WhisperTranscriber.HasContent(text))
        {
            return [];
        }

        var context = string.IsNullOrEmpty(previous) ? text : $"{previous} {text}";
        _context[utterance.Speaker] = context.Length > ContextChars ? context[^ContextChars..] : context;
        return [new TranscriptSegment(utterance.Speaker, utterance.Start, utterance.End, text)];
    }

    public Task ResetContextAsync()
    {
        _context.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Comprueba la clave y el modelo sin enviar audio
    /// (<c>GET /v1/models/{modelo}</c>). Lanza una excepción con el motivo si falla.
    /// </summary>
    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_endpoint, $"models/{Uri.EscapeDataString(Model)}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await DescribeErrorAsync(response, cancellationToken));
        }
    }

    private async Task<string> SendWithRetriesAsync(byte[] wav, string? prompt, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = BuildRequest(wav, prompt);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await ParseTextAsync(response, cancellationToken);
            }

            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (!retryable || attempt >= MaxAttempts)
            {
                throw new InvalidOperationException(await DescribeErrorAsync(response, cancellationToken));
            }

            var delay = response.Headers.RetryAfter?.Delta ?? RetryDelay * attempt;
            await Task.Delay(delay, cancellationToken);
        }
    }

    private HttpRequestMessage BuildRequest(byte[] wav, string? prompt)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(Model), "model" },
            { new StringContent("json"), "response_format" },
        };
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "audio.wav");
        if (!string.Equals(Language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            // gpt-transcribe usa «languages» (lista) en lugar de «language».
            form.Add(new StringContent(Language), "languages[]");
        }

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            form.Add(new StringContent(prompt), "prompt");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_endpoint, "audio/transcriptions")) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return request;
    }

    private static async Task<string> ParseTextAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return json.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? string.Empty
            : string.Empty;
    }

    /// <summary>Mensaje legible para el usuario a partir de la respuesta de error de la API.</summary>
    internal static async Task<string> DescribeErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? apiMessage = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                apiMessage = message.GetString();
            }
        }
        catch (JsonException)
        {
            // Cuerpo sin JSON: basta con el código de estado.
        }

        var reason = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "La clave de OpenAI no es válida o se ha revocado",
            HttpStatusCode.Forbidden => "La clave de OpenAI no tiene acceso a este modelo",
            HttpStatusCode.NotFound => "El modelo indicado no existe en OpenAI",
            HttpStatusCode.TooManyRequests => "OpenAI rechaza más peticiones por ahora (límite de uso o sin saldo)",
            HttpStatusCode.RequestEntityTooLarge => "El audio supera el tamaño que admite OpenAI",
            _ => $"OpenAI respondió {(int)response.StatusCode}",
        };
        return string.IsNullOrWhiteSpace(apiMessage) ? $"{reason}." : $"{reason}: {apiMessage}";
    }
}
