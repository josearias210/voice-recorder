namespace VoiceRecorder.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Procesa instalación y actualizaciones antes de inicializar WPF.
        Velopack.VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
