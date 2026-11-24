using System.Globalization;
using System.Net.Http;
using System.Windows;
using VoiceRecorder.App.Services;
using VoiceRecorder.Transcription;

namespace VoiceRecorder.App;

/// <summary>Ajustes de transcripción: motor por defecto y conexión con OpenAI (clave cifrada con DPAPI).</summary>
public partial class TranscriptionSettingsWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly SettingsService _settings;
    private bool _clearKey;
    private bool _busy;

    public TranscriptionSettingsWindow(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;

        WhisperDefault.IsChecked = settings.DefaultEngine == TranscriptionEngineKind.Whisper;
        OpenAiDefault.IsChecked = settings.DefaultEngine == TranscriptionEngineKind.OpenAi;
        ModelBox.Text = string.IsNullOrWhiteSpace(settings.OpenAiModel) ? OpenAiTranscriber.DefaultModel : settings.OpenAiModel;
        PriceBox.Text = (settings.OpenAiUsdPerMinute ?? OpenAiTranscriber.UsdPerMinute).ToString("0.######", CultureInfo.CurrentCulture);
        UpdateKeyHint();
    }

    /// <summary>Lee el precio admitiendo coma o punto decimal. Null si no es un número positivo.</summary>
    internal static double? ParsePrice(string? text)
    {
        var normalized = (text ?? string.Empty).Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
    }

    private void UpdateKeyHint()
    {
        KeyHint.Text = _settings.HasOpenAiKey && !_clearKey
            ? "Hay una clave guardada (cifrada). Déjalo vacío para conservarla o escribe una nueva para reemplazarla."
            : "La clave se guarda cifrada con DPAPI: solo tu usuario de Windows en este equipo puede leerla.";
    }

    /// <summary>Clave a usar: la escrita ahora o, si está vacía, la guardada.</summary>
    private string? CurrentKey() => KeyBox.Password.Length > 0
        ? KeyBox.Password
        : _clearKey ? null : _settings.GetOpenAiKey();

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var key = CurrentKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            StatusText.Text = "Escribe primero la clave de la API de OpenAI.";
            return;
        }

        _busy = true;
        TestButton.IsEnabled = SaveButton.IsEnabled = false;
        try
        {
            var engine = new OpenAiTranscriber(Http, key, ModelBox.Text);
            StatusText.Text = $"Comprobando la clave con el modelo {engine.Model}…";
            await engine.VerifyAsync(CancellationToken.None);
            StatusText.Text = $"Clave válida: el modelo {engine.Model} está disponible en tu cuenta.";
        }
        catch (Exception ex)
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
        KeyBox.Clear();
        _clearKey = true;
        StatusText.Text = "La clave se borrará al guardar.";
        UpdateKeyHint();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var openAiDefault = OpenAiDefault.IsChecked == true;
        if (openAiDefault && string.IsNullOrWhiteSpace(CurrentKey()))
        {
            StatusText.Text = "Para usar OpenAI por defecto, guarda antes una clave de la API.";
            return;
        }

        if (ParsePrice(PriceBox.Text) is not { } price)
        {
            StatusText.Text = "El precio debe ser un número mayor que cero (por ejemplo, 0,0045).";
            return;
        }

        _settings.OpenAiUsdPerMinute = Math.Abs(price - OpenAiTranscriber.UsdPerMinute) < 1e-12 ? null : price;
        _settings.DefaultEngine = openAiDefault ? TranscriptionEngineKind.OpenAi : TranscriptionEngineKind.Whisper;
        _settings.OpenAiModel = string.IsNullOrWhiteSpace(ModelBox.Text) ? OpenAiTranscriber.DefaultModel : ModelBox.Text.Trim();
        if (KeyBox.Password.Length > 0)
        {
            _settings.SetOpenAiKey(KeyBox.Password);
            KeyBox.Clear();
        }
        else if (_clearKey)
        {
            _settings.SetOpenAiKey(null);
        }

        _settings.Save();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
