using VoiceRecorder.Audio;
using VoiceRecorder.Core;
using VoiceRecorder.Storage;
using VoiceRecorder.Transcription;
using VoiceRecorder.Transcription.Vad;

var seconds = args.Length > 0 && int.TryParse(args[0], out var s) && s is > 0 and <= 120 ? s : 8;
var language = args.Length > 1 ? args[1] : "es";

Console.WriteLine("=== Voice Recorder · Verificador de pipeline (Fase 2) ===");
Console.WriteLine($"Idioma: {language} · Duración: {seconds}s");
Console.WriteLine();

var models = new ModelManager();
var modelInfo = ModelManager.Catalog.First(m => m.FileName == "ggml-base-q5_1.bin");
Console.Write($"Preparando modelo Whisper ({modelInfo.DisplayName})...");
var modelPath = await models.EnsureWhisperModelAsync(
    modelInfo,
    new Progress<double>(p => Console.Write($"\rPreparando modelo Whisper: {p,6:P1}")),
    CancellationToken.None);
Console.WriteLine(" listo");
var vadPath = await models.EnsureVadModelAsync(CancellationToken.None);
Console.WriteLine($"Modelo: {modelPath}");
Console.WriteLine($"VAD:    {vadPath}");
Console.WriteLine();

using var engine = new WhisperTranscriber(modelPath, language);

IVad VadFactory()
{
    try
    {
        var vad = SileroVad.FromFile(vadPath);
        Console.WriteLine($"VAD: Silero activo (ventana {vad.WindowSize} muestras)");
        return vad;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[VAD Silero no disponible: {ex.Message} → usando energía]");
        return new EnergyVad();
    }
}

using var microphone = new MicrophoneCaptureService();
using var loopback = new LoopbackCaptureService();
using var transcriber = new ConversationTranscriber(engine, VadFactory, microphone, loopback);

var segments = new List<TranscriptSegment>();
transcriber.SegmentReady += segment =>
{
    lock (segments)
    {
        segments.Add(segment);
    }

    Console.WriteLine($"[{segment.Start:hh\\:mm\\:ss\\.ff}] {Label(segment.Speaker)}: {segment.Text}");
};
transcriber.ErrorOccurred += ex => Console.WriteLine($"[ERROR] {ex.Message}");

Console.WriteLine();
Console.WriteLine(">>> Se reproducirá voz sintética por los altavoces para simular a la otra parte <<<");
Console.WriteLine();
transcriber.Start();

var ttsThread = new Thread(() =>
{
    try
    {
        using var tts = new System.Speech.Synthesis.SpeechSynthesizer();
        tts.SetOutputToDefaultAudioDevice();
        tts.Speak("Hola, esta es una prueba de transcripción de llamadas en tiempo real.");
        Thread.Sleep(800);
        tts.Speak("Si puedes leer este texto, la captura del audio del sistema funciona correctamente.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[TTS] {ex.Message}");
    }
});
ttsThread.SetApartmentState(ApartmentState.STA);
ttsThread.Start();

await Task.Delay(TimeSpan.FromSeconds(seconds));
await transcriber.StopAsync();
ttsThread.Join(2000);
Console.WriteLine();
Console.WriteLine($"Sesión detenida. Segmentos: {segments.Count}");

if (segments.Count == 0)
{
    Console.WriteLine("Sin segmentos: comprueba que hubo voz en los canales.");
    Environment.Exit(1);
}

var dbPath = Path.Combine(Path.GetTempPath(), $"voice-recorder-test-{Guid.NewGuid():N}.db");
using (var store = new SQLiteSessionStore(dbPath))
{
    lock (segments)
    {
        var id = store.SaveSession("Prueba pipeline", DateTime.Now, transcriber.ActiveElapsed, "test", "", segments);
        var loaded = store.GetSession(id)!.Value;
        Console.WriteLine($"SQLite OK: guardada sesión #{id} con {loaded.Segments.Count} segmentos; historial: {store.ListSessions().Count} sesiones.");
        store.DeleteSession(id);
    }
}

File.Delete(dbPath);
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
try { File.Delete(dbPath); } catch { /* WAL puede tardar en liberar; no es crítico */ }
Console.WriteLine();
Console.WriteLine("Verificación E2E correcta.");
Environment.Exit(0);

static string Label(Speaker speaker) => speaker switch
{
    Speaker.Me => "Yo      ",
    Speaker.Them => "Ellos   ",
    Speaker.Both => "Ambos   ",
    _ => "???     ",
};
