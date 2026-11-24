using System.Windows;
using VoiceRecorder.App.Services;
using VoiceRecorder.App.ViewModels;
using VoiceRecorder.Transcription;

namespace VoiceRecorder.App;

/// <summary>Pregunta si transcribir ahora y con qué motor (propone el motor por defecto).</summary>
public partial class TranscribeDialog : Window
{
    public TranscribeDialog(TranscriptionPrompt prompt, SettingsService? settings, string whisperModelName)
    {
        InitializeComponent();
        Title = prompt.Title;
        HeaderText.Text = prompt.Title;
        MessageText.Text = prompt.Message;
        DeclineButton.Content = prompt.DeclineText;

        WhisperDetail.Text = $" · gratis, en tu equipo (modelo {whisperModelName}).";
        var price = settings?.OpenAiUsdPerMinute ?? OpenAiTranscriber.UsdPerMinute;
        var cost = prompt.AudioDuration > TimeSpan.Zero
            ? $" Como máximo {CostFormat.Cost(prompt.AudioDuration.TotalMinutes * price)} por {CostFormat.Duration(prompt.AudioDuration)} de audio (solo se envían los tramos con voz)."
            : string.Empty;
        OpenAiDetail.Text = $" · en la nube, de pago: envía el audio a OpenAI.{cost}";

        var openAiReady = settings?.HasOpenAiKey == true;
        OpenAiOption.IsEnabled = openAiReady;
        OpenAiHint.Text = openAiReady
            ? string.Empty
            : "Para usar OpenAI, guarda tu clave de la API en «Transcripción…» (barra de ajustes).";

        var useOpenAi = openAiReady && settings?.DefaultEngine == TranscriptionEngineKind.OpenAi;
        OpenAiOption.IsChecked = useOpenAi;
        WhisperOption.IsChecked = !useOpenAi;
    }

    /// <summary>Motor elegido, o null si el usuario decide no transcribir.</summary>
    public TranscriptionEngineKind? SelectedEngine { get; private set; }

    private void Transcribe_Click(object sender, RoutedEventArgs e)
    {
        SelectedEngine = OpenAiOption.IsChecked == true ? TranscriptionEngineKind.OpenAi : TranscriptionEngineKind.Whisper;
        DialogResult = true;
    }

    private void Decline_Click(object sender, RoutedEventArgs e)
    {
        SelectedEngine = null;
        DialogResult = false;
    }
}
