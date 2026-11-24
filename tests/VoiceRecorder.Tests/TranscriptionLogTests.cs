using VoiceRecorder.Core;
using VoiceRecorder.Storage;

namespace VoiceRecorder.Tests;

/// <summary>Registro de transcripciones: coste y tiempo por mes, independiente de las sesiones.</summary>
public sealed class TranscriptionLogTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), $"vr-costs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static TranscriptionRun Whisper(DateTime at, double minutes, double elapsedMinutes) =>
        new(at, "Whisper", "Large v3 Turbo", TimeSpan.FromMinutes(elapsedMinutes), minutes * 60, 0, 1, "Llamada");

    private static TranscriptionRun OpenAi(DateTime at, double minutes, bool completed = true) =>
        new(at, "OpenAI", "gpt-transcribe", TimeSpan.FromMinutes(1), minutes * 60, minutes * 0.0045, 2, "Llamada", completed);

    [Fact]
    public void Append_Y_ReadAll_Conservan_Los_Datos_Del_Mas_Reciente_Al_Mas_Antiguo()
    {
        var log = new TranscriptionLog(_dir);
        log.Append(Whisper(new DateTime(2026, 10, 3, 9, 0, 0), 30, 12));
        log.Append(OpenAi(new DateTime(2026, 11, 20, 18, 0, 0), 10, completed: false));

        var runs = new TranscriptionLog(_dir).ReadAll();

        Assert.Equal(2, runs.Count);
        Assert.Equal("OpenAI", runs[0].Engine);
        Assert.False(runs[0].Completed);
        Assert.Equal(0.045, runs[0].CostUsd, 6);
        Assert.Equal(TimeSpan.FromMinutes(12), runs[1].Elapsed);
        Assert.Equal("Large v3 Turbo", runs[1].Model);
    }

    [Fact]
    public void Lineas_Danadas_Se_Ignoran()
    {
        var log = new TranscriptionLog(_dir);
        log.Append(Whisper(new DateTime(2026, 11, 1), 5, 2));
        File.AppendAllText(log.FilePath, "{ esto no es json\n");
        log.Append(Whisper(new DateTime(2026, 11, 2), 5, 2));

        Assert.Equal(2, log.ReadAll().Count);
    }

    [Fact]
    public void Sin_Archivo_No_Hay_Transcripciones()
    {
        Assert.Empty(new TranscriptionLog(_dir).ReadAll());
    }

    [Fact]
    public void SummarizeByMonth_Suma_Coste_Tiempo_Y_Audio_Por_Mes()
    {
        var runs = new[]
        {
            Whisper(new DateTime(2026, 11, 2, 10, 0, 0), 60, 20),
            OpenAi(new DateTime(2026, 11, 15, 10, 0, 0), 40),
            OpenAi(new DateTime(2026, 11, 30, 23, 59, 0), 20),
            OpenAi(new DateTime(2026, 10, 31, 23, 0, 0), 100),
        };

        var months = TranscriptionLog.SummarizeByMonth(runs);

        Assert.Equal(2, months.Count);
        var november = months[0];
        Assert.Equal(new DateTime(2026, 11, 1), november.Month);
        Assert.Equal(3, november.Runs);
        Assert.Equal(2, november.PaidRuns);
        Assert.Equal(60 * 0.0045, november.CostUsd, 6);
        Assert.Equal(60 * 60, november.PaidAudioSeconds, 6);
        Assert.Equal(120 * 60, november.AudioSeconds, 6);
        Assert.Equal(TimeSpan.FromMinutes(22), november.Elapsed);
        Assert.Equal(100 * 0.0045, months[1].CostUsd, 6);
    }

    [Fact]
    public void FormatMonth_Usa_El_Nombre_En_Espanol()
    {
        Assert.Equal("noviembre de 2026", TranscriptionLog.FormatMonth(new DateTime(2026, 11, 1)));
    }
}
