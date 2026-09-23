using VoiceRecorder.Audio;
using VoiceRecorder.Core;

// Verificación de Fase 1: captura dual (micrófono + loopback) durante N segundos
// y vuelca cada canal a un WAV mono 16kHz para revisión auditiva.
var seconds = args.Length > 0 && int.TryParse(args[0], out var s) && s is > 0 and <= 120
    ? s
    : 6;

var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "verification");
Directory.CreateDirectory(outputDir);

Console.WriteLine("=== Voice Recorder · Verificador de captura (Fase 1) ===");
Console.WriteLine();
Console.WriteLine("Salidas disponibles:");
foreach (var device in new WasapiDeviceEnumerator().ListOutputs())
{
    Console.WriteLine($"  - {device.Name}");
}

Console.WriteLine("Micrófonos disponibles:");
foreach (var device in new WasapiDeviceEnumerator().ListMicrophones())
{
    Console.WriteLine($"  - {device.Name}");
}

Console.WriteLine();

var chunks = new Dictionary<AudioSource, List<AudioChunk>>
{
    [AudioSource.Microphone] = [],
    [AudioSource.Loopback] = [],
};

var failures = new List<string>();
using var microphone = new MicrophoneCaptureService();
using var loopback = new LoopbackCaptureService();

void OnChunkReady(object? sender, AudioChunk chunk)
{
    lock (chunks)
    {
        chunks[chunk.Source].Add(chunk);
    }
}

microphone.AudioChunkReady += OnChunkReady;
loopback.AudioChunkReady += OnChunkReady;
microphone.CaptureFailed += (_, ex) => { lock (failures) failures.Add($"Micrófono: {ex.Message}"); };
loopback.CaptureFailed += (_, ex) => { lock (failures) failures.Add($"Loopback: {ex.Message}"); };

Console.WriteLine($"Grabando {seconds}s en ambos canales...");
Console.WriteLine("(habla frente al micrófono o reproduce un vídeo para generar señal)");
microphone.Start();
loopback.Start();
await Task.Delay(TimeSpan.FromSeconds(seconds));
microphone.Stop();
loopback.Stop();
await Task.Delay(500);

var ok = true;
lock (chunks)
{
    foreach (var (source, list) in chunks)
    {
        var name = source == AudioSource.Microphone ? "mic" : "loopback";
        var path = Path.Combine(outputDir, $"{name}.wav");
        WavDumpWriter.Write(path, list);

        var totalSamples = list.Sum(c => (long)c.Samples.Length);
        var durationSeconds = totalSamples / (double)AudioConstants.TargetSampleRate;
        double sumSquares = 0;
        foreach (var chunk in list)
        {
            foreach (var sample in chunk.Samples)
            {
                sumSquares += (double)sample * sample;
            }
        }

        var rms = totalSamples > 0 ? Math.Sqrt(sumSquares / totalSamples) : 0;
        var hasSignal = rms > 0.0005;
        ok &= hasSignal && durationSeconds > 0;

        Console.WriteLine();
        Console.WriteLine($"[{name}]");
        Console.WriteLine($"  Duración: {durationSeconds:F2}s (esperados ~{seconds}s)");
        Console.WriteLine($"  RMS: {rms:F5} -> {(hasSignal ? "SEÑAL detectada" : "SILENCIO")}");
        Console.WriteLine($"  WAV: {Path.GetFullPath(path)}");
    }
}

lock (failures)
{
    foreach (var failure in failures)
    {
        ok = false;
        Console.WriteLine($"[FALLO] {failure}");
    }
}

Console.WriteLine();
Console.WriteLine(ok
    ? "Verificación OK: ambos canales capturaron audio con señal."
    : "Verificación INCOMPLETA: revisa dispositivos o reproduce audio durante la prueba.");
return ok ? 0 : 1;
