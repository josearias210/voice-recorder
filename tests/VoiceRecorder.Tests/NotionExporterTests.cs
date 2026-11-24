using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using VoiceRecorder.Core;
using VoiceRecorder.Storage.Notion;

namespace VoiceRecorder.Tests;

/// <summary>Upload to Notion against a fake server (no network).</summary>
public class NotionExporterTests
{
    private const string DatabaseId = "0123456789abcdef0123456789abcdef";
    private const string CanonicalId = "01234567-89ab-cdef-0123-456789abcdef";

    private const string Database =
        """
        {"object":"database","title":[{"plain_text":"Llamadas"}],
         "properties":{
           "Nombre":{"type":"title"},
           "Fecha":{"type":"date"},
           "Etiquetas":{"type":"multi_select"},
           "Duración":{"type":"number"},
           "Otro":{"type":"rich_text"}}}
        """;

    private sealed class FakeNotion : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, JsonNode? Body, HttpRequestMessage Request)> Requests { get; } = [];

        public string DatabaseJson { get; init; } = Database;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var text = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add((request.Method, path, text is null ? null : JsonNode.Parse(text), request));
            var json = (request.Method.Method, path) switch
            {
                ("GET", _) when path.StartsWith("/v1/databases/", StringComparison.Ordinal) => DatabaseJson,
                ("POST", "/v1/pages") => """{"id":"page-1","url":"https://www.notion.so/page-1"}""",
                ("PATCH", "/v1/blocks/page-1/children") when text!.Contains("is_toggleable") => """{"results":[{"id":"toggle-1"}]}""",
                _ => """{"results":[]}""",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static SessionSummary Session(CallSummary? summary = null) => new(
        7,
        "Llamada con cliente",
        new DateTime(2026, 11, 24, 10, 30, 0),
        TimeSpan.FromMinutes(12.5),
        "trabajo, cliente, trabajo",
        "Preparar oferta",
        2,
        CallSummary: summary);

    [Theory]
    [InlineData(DatabaseId)]
    [InlineData(CanonicalId)]
    [InlineData("https://www.notion.so/miequipo/Llamadas-0123456789abcdef0123456789abcdef?v=fedcba9876543210fedcba9876543210")]
    [InlineData("https://www.notion.so/0123456789ABCDEF0123456789ABCDEF")]
    public void ParseId_Acepta_Enlaces_E_Ids(string input) =>
        Assert.Equal(CanonicalId, NotionExporter.ParseId(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://www.notion.so/sin-id")]
    public void ParseId_Sin_Id_Devuelve_Null(string? input) => Assert.Null(NotionExporter.ParseId(input));

    [Fact]
    public void Encuentra_Las_Propiedades_Por_Tipo_Y_Nombre()
    {
        var info = NotionExporter.ParseDatabaseInfo(JsonNode.Parse(Database)!.AsObject());

        Assert.Equal("Llamadas", info.Title);
        Assert.Equal("Nombre", info.TitleProperty);
        Assert.Equal("Fecha", info.DateProperty);
        Assert.Equal("Etiquetas", info.TagsProperty);
        Assert.Equal("Duración", info.DurationProperty);
        Assert.True(info.DurationIsNumber);
    }

    [Fact]
    public void Sin_Propiedades_Opcionales_Solo_Rellena_El_Titulo()
    {
        var info = NotionExporter.ParseDatabaseInfo(JsonNode.Parse("""{"properties":{"Name":{"type":"title"},"Otro":{"type":"number"}}}""")!.AsObject());
        var properties = NotionExporter.BuildProperties(Session(), info);

        Assert.Null(info.DateProperty);
        Assert.Null(info.TagsProperty);
        Assert.Null(info.DurationProperty);
        var property = Assert.Single(properties);
        Assert.Equal("Name", property.Key);
        Assert.Equal("Llamada con cliente", property.Value!["title"]![0]!["text"]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void Las_Propiedades_Llevan_Fecha_Etiquetas_Sin_Duplicar_Y_Minutos()
    {
        var info = NotionExporter.ParseDatabaseInfo(JsonNode.Parse(Database)!.AsObject());
        var properties = NotionExporter.BuildProperties(Session(), info);

        Assert.Equal("2026-11-24T10:30:00", properties["Fecha"]!["date"]!["start"]!.GetValue<string>());
        var tags = properties["Etiquetas"]!["multi_select"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(["trabajo", "cliente"], tags);
        Assert.Equal(12.5, properties["Duración"]!["number"]!.GetValue<double>());
    }

    [Fact]
    public void El_Markdown_Del_Resumen_Se_Convierte_En_Bloques()
    {
        var blocks = NotionExporter.MarkdownToBlocks("## Resumen\nTexto **importante**.\n\n- Punto\n- [ ] Tarea\n- [x] Hecha\n1. Primero\n### Detalle");

        Assert.Equal(
            ["heading_2", "paragraph", "bulleted_list_item", "to_do", "to_do", "numbered_list_item", "heading_3"],
            blocks.Select(b => b["type"]!.GetValue<string>()).ToList());
        Assert.Equal("Texto importante.", blocks[1]["paragraph"]!["rich_text"]![0]!["text"]!["content"]!.GetValue<string>());
        Assert.False(blocks[3]["to_do"]!["checked"]!.GetValue<bool>());
        Assert.True(blocks[4]["to_do"]!["checked"]!.GetValue<bool>());
        Assert.Equal("Tarea", blocks[3]["to_do"]!["rich_text"]![0]!["text"]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void Los_Textos_Largos_Se_Dividen_En_Trozos_Del_Limite_De_Notion()
    {
        var richText = NotionExporter.RichText(new string('a', NotionExporter.MaxTextLength * 2 + 5));

        Assert.Equal(3, richText.Count);
        Assert.Equal(5, richText[2]!["text"]!["content"]!.GetValue<string>().Length);
    }

    [Fact]
    public async Task Crea_La_Pagina_Con_Resumen_Y_La_Transcripcion_Plegable_En_Lotes()
    {
        var handler = new FakeNotion();
        var exporter = new NotionExporter(new HttpClient(handler), "ntn_prueba", DatabaseId);
        var segments = Enumerable.Range(0, 150)
            .Select(i => new TranscriptSegment(i % 2 == 0 ? Speaker.Me : Speaker.Them, TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(i + 1), $"frase {i}"))
            .ToList();

        var export = await exporter.ExportAsync(Session(new CallSummary("## Resumen\n- Punto", DateTime.Now, "gpt-5-mini", 0.01)), segments, NotionContent.Summary | NotionContent.Transcript, CancellationToken.None);

        Assert.Equal("page-1", export.PageId);
        Assert.Equal("https://www.notion.so/page-1", export.Url);

        var get = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, get.Method);
        Assert.Equal($"/v1/databases/{CanonicalId}", get.Path);
        Assert.Equal("ntn_prueba", get.Request.Headers.Authorization!.Parameter);
        Assert.Equal(NotionExporter.ApiVersion, get.Request.Headers.GetValues("Notion-Version").Single());

        var create = handler.Requests[1];
        Assert.Equal("/v1/pages", create.Path);
        Assert.Equal(CanonicalId, create.Body!["parent"]!["database_id"]!.GetValue<string>());
        var children = create.Body["children"]!.AsArray().Select(b => b!["type"]!.GetValue<string>()).ToList();
        Assert.Equal(["callout", "heading_2", "bulleted_list_item"], children);

        var toggle = handler.Requests[2];
        Assert.Equal("/v1/blocks/page-1/children", toggle.Path);
        Assert.True(toggle.Body!["children"]![0]!["heading_2"]!["is_toggleable"]!.GetValue<bool>());

        // 150 segments → two batches (100 + 50) inside the toggle.
        var batches = handler.Requests.Skip(3).ToList();
        Assert.All(batches, b => Assert.Equal("/v1/blocks/toggle-1/children", b.Path));
        Assert.Equal([100, 50], batches.Select(b => b.Body!["children"]!.AsArray().Count).ToList());
        var first = batches[0].Body!["children"]![0]!["paragraph"]!["rich_text"]!;
        Assert.Equal("[00:00:00] Yo: ", first[0]!["text"]!["content"]!.GetValue<string>());
        Assert.True(first[0]!["annotations"]!["bold"]!.GetValue<bool>());
        Assert.Equal("frase 0", first[1]!["text"]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Solo_Sube_Lo_Elegido_Resumen_Sin_Transcripcion()
    {
        var handler = new FakeNotion();
        var exporter = new NotionExporter(new HttpClient(handler), "ntn_prueba", DatabaseId);
        var session = Session(new CallSummary("## Resumen\n- Punto", DateTime.Now, "gpt-5-mini", 0.01));
        var segments = new List<TranscriptSegment> { new(Speaker.Me, TimeSpan.Zero, TimeSpan.FromSeconds(1), "hola") };

        await exporter.ExportAsync(session, segments, NotionContent.Summary, CancellationToken.None);

        Assert.Equal(["GET", "POST"], handler.Requests.Select(r => r.Method.Method).ToList());
        var children = handler.Requests[1].Body!["children"]!.AsArray().Select(b => b!["type"]!.GetValue<string>()).ToList();
        Assert.Equal(["callout", "heading_2", "bulleted_list_item"], children);
    }

    [Fact]
    public async Task Transcripcion_Y_Analisis_Van_En_Encabezados_Plegables_Sin_El_Resumen()
    {
        var handler = new FakeNotion();
        var exporter = new NotionExporter(new HttpClient(handler), "ntn_prueba", DatabaseId);
        var session = Session(new CallSummary("## Resumen\n- Punto", DateTime.Now, "gpt-5-mini", 0.01)) with
        {
            Review = new CallSummary("## Fallas\n- Interrumpí", DateTime.Now, "gpt-5-mini", 0.02),
        };
        var segments = new List<TranscriptSegment> { new(Speaker.Me, TimeSpan.Zero, TimeSpan.FromSeconds(1), "hola") };

        await exporter.ExportAsync(session, segments, NotionContent.Transcript | NotionContent.Review, CancellationToken.None);

        var created = handler.Requests[1].Body!["children"]!.AsArray().Select(b => b!["type"]!.GetValue<string>()).ToList();
        Assert.Equal(["callout"], created); // Only the notes: the summary was not chosen.
        var toggles = handler.Requests
            .Where(r => r.Path == "/v1/blocks/page-1/children")
            .Select(r => r.Body!["children"]![0]!["heading_2"]!["rich_text"]![0]!["text"]!["content"]!.GetValue<string>())
            .ToList();
        Assert.Equal(["Análisis de comunicación", "Transcripción"], toggles);
        var reviewBlocks = handler.Requests.First(r => r.Path == "/v1/blocks/toggle-1/children").Body!["children"]!.AsArray();
        Assert.Equal("heading_3", reviewBlocks[0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Si_No_Hay_Nada_De_Lo_Elegido_No_Crea_La_Pagina()
    {
        var handler = new FakeNotion();
        var exporter = new NotionExporter(new HttpClient(handler), "ntn_prueba", DatabaseId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => exporter.ExportAsync(Session(), [], NotionContent.Summary | NotionContent.Review, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void WhatWillBeUploaded_Solo_Cuenta_Lo_Que_Existe()
    {
        var segments = new List<TranscriptSegment> { new(Speaker.Me, TimeSpan.Zero, TimeSpan.FromSeconds(1), "hola") };
        var all = NotionContent.Summary | NotionContent.Transcript | NotionContent.Review;

        Assert.Equal(NotionContent.Transcript, NotionExporter.WhatWillBeUploaded(Session(), segments, all));
        Assert.Equal(NotionContent.None, NotionExporter.WhatWillBeUploaded(Session(), [], all));
    }

    [Fact]
    public async Task Base_De_Datos_No_Compartida_Da_Un_Error_Claro()
    {
        // Notion answers 404 when the database is not shared with the integration.
        using var handler = new StatusHandler(HttpStatusCode.NotFound, """{"object":"error","message":"Could not find database"}""");
        using var http = new HttpClient(handler);
        var exporter = new NotionExporter(http, "ntn_prueba", DatabaseId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.GetDatabaseInfoAsync(CancellationToken.None));

        Assert.Contains("Conexiones", error.Message);
        Assert.Contains("Could not find database", error.Message);
    }

    [Fact]
    public void Un_Id_Invalido_Se_Rechaza_Al_Crear_El_Exportador()
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(() => new NotionExporter(http, "ntn_prueba", "no es un id"));
    }

    private sealed class StatusHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        // The response (and its content) is owned and disposed by the caller.
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
