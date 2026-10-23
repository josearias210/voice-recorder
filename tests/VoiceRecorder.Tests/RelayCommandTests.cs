using VoiceRecorder.App.Infrastructure;

namespace VoiceRecorder.Tests;

public class RelayCommandTests
{
    [Fact]
    public void Comando_Deshabilitado_No_Ejecuta_Desde_La_Bandeja()
    {
        var calls = 0;
        var command = new RelayCommand(_ => calls++, _ => false);

        command.Execute(null);

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Comando_Asincrono_Deshabilitado_No_Ejecuta()
    {
        var calls = 0;
        var command = new AsyncRelayCommand(_ =>
        {
            calls++;
            return Task.CompletedTask;
        }, _ => false);

        command.Execute(null);

        Assert.Equal(0, calls);
        Assert.False(command.IsRunning);
    }

    [Fact]
    public async Task Comando_Asincrono_Impide_Ejecuciones_Simultaneas_Y_Permite_Reintentar()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var command = new AsyncRelayCommand(_ =>
        {
            calls++;
            return pending.Task;
        });

        command.Execute(null);
        command.Execute(null);
        Assert.Equal(1, calls);
        Assert.False(command.CanExecute(null));

        pending.SetResult();
        await Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (command.IsRunning)
            {
                await Task.Delay(10, timeout.Token);
            }
        });

        command.Execute(null);
        Assert.Equal(2, calls);
        Assert.True(command.CanExecute(null));
    }
}
