using System.Diagnostics;
using System.IO;

namespace VoiceRecorder.App.Services;

/// <summary>
/// Aviso legal que el usuario debe aceptar una vez (y de nuevo si cambia
/// <see cref="Version"/>) antes de usar la aplicación.
/// </summary>
public static class LegalNotice
{
    /// <summary>Versión del texto del aviso. Súbela cuando cambien DISCLAIMER.md o PRIVACY.md de forma relevante.</summary>
    public const string Version = "2026-11-19";

    public const string Title = "Voice Recorder · Aviso legal";

    public const string Summary =
        "Antes de usar Voice Recorder, lee y acepta estas condiciones:\n\n" +
        "• La aplicación se ofrece «TAL CUAL», sin garantías (licencia MIT). El autor no se hace responsable de ningún daño, pérdida de datos ni consecuencia derivada de su uso.\n\n" +
        "• TÚ ERES EL ÚNICO RESPONSABLE del uso que hagas de ella. Grabar o transcribir una conversación puede requerir el consentimiento de todas las personas que participan, según la ley de tu país (por ejemplo, el RGPD en la UE). Informa a los demás y obtén su consentimiento.\n\n" +
        "• NO está permitido usarla para grabar a escondidas, espiar ni vigilar a otras personas. La aplicación siempre es visible, solo graba cuando pulsas Iniciar y no envía nada a nadie.\n\n" +
        "• Las transcripciones automáticas contienen errores y no son un registro fiel de la conversación.\n\n" +
        "• Todo se procesa en tu equipo: no hay telemetría ni se envían tus conversaciones a ningún servidor.\n\n" +
        "El texto completo está en DISCLAIMER.md y PRIVACY.md, en la carpeta de la aplicación y en el botón «Aviso legal».\n\n" +
        "¿Aceptas estas condiciones? Si eliges «No», la aplicación se cerrará.";

    /// <summary>Indica si hay que mostrar el aviso (nunca aceptado o aceptado en una versión anterior).</summary>
    public static bool RequiresAcceptance(string? acceptedVersion) =>
        !string.Equals(acceptedVersion, Version, StringComparison.Ordinal);

    /// <summary>Abre DISCLAIMER.md (o PRIVACY.md) incluido en el paquete con el Bloc de notas.</summary>
    public static void OpenDocument(string fileName)
    {
        var path = Path.Join(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = false });
        }
        catch
        {
            // Sin Bloc de notas: el archivo sigue disponible en la carpeta de instalación.
        }
    }
}
