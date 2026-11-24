using System.Globalization;
using System.Windows;
using VoiceRecorder.App.Services;
using VoiceRecorder.Transcription;

namespace VoiceRecorder.App;

/// <summary>Summary settings: automatic or on-demand, OpenAI model and prices for cost estimates.</summary>
public partial class SummarySettingsWindow : Window
{
    private readonly SettingsService _settings;

    public SummarySettingsWindow(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;

        AutoBox.IsChecked = settings.AutoSummarize;
        ModelBox.Text = string.IsNullOrWhiteSpace(settings.SummaryModel) ? OpenAiSummarizer.DefaultModel : settings.SummaryModel;
        InputPriceBox.Text = (settings.SummaryUsdPerMillionInput ?? OpenAiSummarizer.DefaultUsdPerMillionInput).ToString("0.######", CultureInfo.CurrentCulture);
        OutputPriceBox.Text = (settings.SummaryUsdPerMillionOutput ?? OpenAiSummarizer.DefaultUsdPerMillionOutput).ToString("0.######", CultureInfo.CurrentCulture);
        KeyHint.Text = settings.HasOpenAiKey
            ? "Se usará la clave de OpenAI guardada en «Transcripción…»."
            : "Todavía no hay clave de OpenAI: guárdala en «Transcripción…» para poder generar resúmenes.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TranscriptionSettingsWindow.ParsePrice(InputPriceBox.Text) is not { } input
            || TranscriptionSettingsWindow.ParsePrice(OutputPriceBox.Text) is not { } output)
        {
            StatusText.Text = "Los precios deben ser números mayores que cero (por ejemplo, 0,25).";
            return;
        }

        if (AutoBox.IsChecked == true && !_settings.HasOpenAiKey)
        {
            StatusText.Text = "Para generar resúmenes automáticos, guarda antes la clave de OpenAI en «Transcripción…».";
            return;
        }

        _settings.AutoSummarize = AutoBox.IsChecked == true;
        _settings.SummaryModel = string.IsNullOrWhiteSpace(ModelBox.Text) || ModelBox.Text.Trim() == OpenAiSummarizer.DefaultModel
            ? null
            : ModelBox.Text.Trim();
        _settings.SummaryUsdPerMillionInput = Math.Abs(input - OpenAiSummarizer.DefaultUsdPerMillionInput) < 1e-12 ? null : input;
        _settings.SummaryUsdPerMillionOutput = Math.Abs(output - OpenAiSummarizer.DefaultUsdPerMillionOutput) < 1e-12 ? null : output;
        _settings.Save();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
