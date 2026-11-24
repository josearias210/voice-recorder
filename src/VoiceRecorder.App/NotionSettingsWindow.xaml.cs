using System.Net.Http;
using System.Windows;
using VoiceRecorder.App.Services;
using VoiceRecorder.Storage.Notion;

namespace VoiceRecorder.App;

/// <summary>Notion destination: integration token (encrypted with DPAPI), database and whether to ask after transcribing.</summary>
public partial class NotionSettingsWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly SettingsService _settings;
    private bool _clearToken;
    private bool _busy;

    public NotionSettingsWindow(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;

        EnabledBox.IsChecked = settings.NotionEnabled;
        DatabaseBox.Text = settings.NotionDatabaseId ?? string.Empty;
        AskBox.IsChecked = settings.NotionAskAfterTranscription;
        IncludeSummaryBox.IsChecked = settings.NotionIncludeSummary;
        IncludeTranscriptBox.IsChecked = settings.NotionIncludeTranscript;
        IncludeReviewBox.IsChecked = settings.NotionIncludeReview && settings.ReviewEnabled;
        IncludeReviewBox.IsEnabled = settings.ReviewEnabled;
        UpdateTokenHint();
    }

    private void UpdateTokenHint()
    {
        TokenHint.Text = _settings.HasNotionToken && !_clearToken
            ? "Hay un token guardado (cifrado). Déjalo vacío para conservarlo o escribe uno nuevo para reemplazarlo."
            : "El token se guarda cifrado con DPAPI: solo tu usuario de Windows en este equipo puede leerlo.";
    }

    /// <summary>Token to use: the one typed now or, if empty, the saved one.</summary>
    private string? CurrentToken() => TokenBox.Password.Length > 0
        ? TokenBox.Password
        : _clearToken ? null : _settings.GetNotionToken();

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var token = CurrentToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            StatusText.Text = "Escribe primero el token de la integración de Notion.";
            return;
        }

        if (NotionExporter.ParseId(DatabaseBox.Text) is null)
        {
            StatusText.Text = "Pega el enlace o el Id de la base de datos de Notion.";
            return;
        }

        _busy = true;
        TestButton.IsEnabled = SaveButton.IsEnabled = false;
        try
        {
            StatusText.Text = "Comprobando la conexión con Notion…";
            var info = await new NotionExporter(Http, token, DatabaseBox.Text).GetDatabaseInfoAsync(CancellationToken.None);
            var extras = new[]
            {
                info.DateProperty is null ? null : $"fecha → «{info.DateProperty}»",
                info.TagsProperty is null ? null : $"etiquetas → «{info.TagsProperty}»",
                info.DurationProperty is null ? null : $"duración → «{info.DurationProperty}»",
            }.Where(x => x is not null);
            var extrasText = string.Join(", ", extras);
            StatusText.Text = $"Conexión correcta con «{info.Title}». Título → «{info.TitleProperty}»"
                + (extrasText.Length > 0 ? $"; {extrasText}." : ". No hay propiedades de fecha, etiquetas ni duración: solo se rellenará el título.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or HttpRequestException or TaskCanceledException)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            TestButton.IsEnabled = SaveButton.IsEnabled = true;
            _busy = false;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        TokenBox.Clear();
        _clearToken = true;
        StatusText.Text = "El token se borrará al guardar.";
        UpdateTokenHint();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var enabled = EnabledBox.IsChecked == true;
        var databaseId = NotionExporter.ParseId(DatabaseBox.Text);
        if (enabled && (string.IsNullOrWhiteSpace(CurrentToken()) || databaseId is null))
        {
            StatusText.Text = "Para usar Notion, indica el token de la integración y el enlace de la base de datos.";
            return;
        }

        var includeSummary = IncludeSummaryBox.IsChecked == true;
        var includeTranscript = IncludeTranscriptBox.IsChecked == true;
        var includeReview = IncludeReviewBox.IsChecked == true && _settings.ReviewEnabled;
        if (enabled && !includeSummary && !includeTranscript && !includeReview)
        {
            StatusText.Text = "Marca al menos qué se sube: resumen, transcripción o análisis.";
            return;
        }

        _settings.NotionIncludeSummary = includeSummary;
        _settings.NotionIncludeTranscript = includeTranscript;
        if (_settings.ReviewEnabled)
        {
            _settings.NotionIncludeReview = includeReview;
        }

        _settings.NotionEnabled = enabled;
        _settings.NotionDatabaseId = databaseId;
        _settings.NotionAskAfterTranscription = AskBox.IsChecked == true;
        if (TokenBox.Password.Length > 0)
        {
            _settings.SetNotionToken(TokenBox.Password);
            TokenBox.Clear();
        }
        else if (_clearToken)
        {
            _settings.SetNotionToken(null);
        }

        _settings.Save();
        DialogResult = true;
    }

    private void OpenIntegrations_Click(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.notion.so/profile/integrations") { UseShellExecute = true });

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
