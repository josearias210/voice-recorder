# ⚠️ Aviso legal y exención de responsabilidad — Voice Recorder

> Última actualización: 2026-11-23 · Aplica a partir de la versión 1.4.0

## 0. Aceptación

Al descargar, instalar, ejecutar o usar Voice Recorder **aceptas íntegramente este aviso** y la [política de privacidad](PRIVACY.md). La aplicación muestra un resumen de este aviso **la primera vez que se abre** (y de nuevo cuando el aviso cambia) y **no se puede usar sin aceptarlo**; la aceptación queda registrada en tu equipo (`settings.json`). Si no estás de acuerdo, no uses la aplicación y desinstálala.

## 1. Licencia y falta de garantías

Voice Recorder es software gratuito y de código abierto que se proporciona **«TAL CUAL» (AS IS) y «SEGÚN DISPONIBILIDAD»**, sin garantías ni condiciones de ningún tipo, expresas o implícitas, según la licencia [MIT](LICENSE). En la máxima medida permitida por la ley aplicable:

- **El autor y los colaboradores NO se hacen responsables** de ningún daño directo, indirecto, incidental, especial o consecuente, perjuicio, pérdida de datos, pérdida de beneficios, interrupción de negocio o cualquier otro incidente derivado del uso, el mal uso o la imposibilidad de uso de esta aplicación, aunque se hubiera advertido de su posibilidad.
- No se garantiza que la aplicación funcione sin errores, sea ininterrumpida, segura frente a todo riesgo, produzca transcripciones exactas o sea apta para un propósito concreto. **La transcripción automática contiene errores**: no confíes en ella como registro fiel, completo o válido de una conversación, ni como prueba.
- No existe obligación de ofrecer soporte, mantenimiento, actualizaciones ni correcciones.

## 2. Responsabilidad exclusiva del usuario

El usuario es **el único y exclusivo responsable** del uso que haga de la aplicación y de sus resultados. El autor **no se hace responsable**, entre otros, de:

- El uso de la aplicación para **grabar o transcribir conversaciones sin el conocimiento o consentimiento** de las personas involucradas.
- Cualquier uso contrario a la ley, a los derechos de terceros (intimidad, protección de datos, secreto de las comunicaciones, propiedad intelectual) o a las normas internas de tu empresa u organización.
- El tratamiento posterior (almacenamiento, difusión, publicación, compartición) de las grabaciones o transcripciones que realices.
- Cualquier consecuencia legal, laboral, personal o económica derivada de dicho uso.

## 3. Usos no permitidos

No uses Voice Recorder para:

- Vigilar, espiar o grabar a personas de forma oculta, o sin una base legal válida.
- Grabar comunicaciones en las que no participas o a las que no tienes derecho a acceder.
- Cualquier actividad ilícita o que vulnere derechos de terceros.

La aplicación está pensada para que **un participante de una llamada** transcriba su propia conversación con conocimiento de los demás.

### Diseño transparente: lo que la aplicación no hace ni hará

Voice Recorder **no está concebida ni adaptada para interceptar comunicaciones ajenas ni para vigilar a nadie**, y el proyecto mantiene ese diseño de forma deliberada:

- **Siempre es visible**: tiene ventana e icono en la bandeja del sistema, y el estado de la sesión (grabando, en pausa, detenida) se muestra en ambos. No existe un modo oculto, invisible ni «sigiloso».
- **Solo graba cuando tú lo pides**: la captura empieza al pulsar *Iniciar* y termina con *Pausar* o *Detener*. No graba en segundo plano por su cuenta ni al arrancar Windows.
- **No envía nada por su cuenta**: no hay envío remoto automático, sincronización en segundo plano ni reenvío de grabaciones o transcripciones a terceros. Solo salen datos en dos casos, siempre porque tú lo eliges en ese momento: (1) si eliges transcribir con **OpenAI**, los tramos con voz del audio se envían a la API de OpenAI con **tu propia** clave, y (2) el **backup externo opcional** (desactivado por defecto), que solo se ejecuta cuando pulsas *Sincronizar…* y copia a **tu propia** cuenta de Azure Blob Storage. Por defecto la transcripción se hace en tu equipo con Whisper.
- **Solo captura el equipo en el que se ejecuta**: el micrófono y el audio que reproduce ese mismo PC. No accede a otros dispositivos, cuentas ni comunicaciones en las que el usuario no participa.

Las propuestas o contribuciones que añadan funciones de ocultación, grabación encubierta, arranque silencioso o envío remoto de grabaciones **no se aceptarán**, y no se dará soporte a consultas cuyo fin sea un uso no permitido.

## 4. Grabar conversaciones puede requerir consentimiento

Dependiendo de tu jurisdicción (España y el RGPD en la UE, leyes de consentimiento de una o de todas las partes en otros países, normativa laboral, etc.), **grabar o transcribir una llamada, incluso tu propia llamada, puede exigir el consentimiento de todas las personas intervinientes o una base legal válida**. Antes de usar la aplicación:

- Infórmate de la legislación aplicable en tu país y contexto (personal, laboral, periodístico…).
- Obtén los consentimientos necesarios y comunica con transparencia que la conversación se está transcribiendo.
- Trata las grabaciones y transcripciones como lo que son: **datos personales de terceros**.

Esta aplicación es una herramienta técnica neutra; **no sustituye asesoramiento legal** y su existencia no implica autorización para ningún uso concreto.

## 5. Datos de terceros

Si transcribes llamadas en las que participan otras personas, las transcripciones y el audio (que siempre se graba) contienen datos personales de esas personas, que quedan **bajo tu control exclusivo en tu equipo**. A efectos de la normativa de protección de datos, **el responsable del tratamiento eres tú**, no el autor de la aplicación, que no recibe, almacena ni tiene acceso a ninguno de esos datos. Cómo los guardes, compartas o elimines es responsabilidad tuya.

Si eliges transcribir con **OpenAI**, el audio con voz de esas personas se envía a un tercero (OpenAI) que lo trata según sus propias condiciones y su [política de privacidad](https://openai.com/policies/privacy-policy/). Asegúrate de que el consentimiento o la base legal con la que grabas cubre también ese envío; si no, usa Whisper local.

## 6. Indemnidad

En la medida en que lo permita la ley, te comprometes a **mantener indemnes al autor y a los colaboradores** frente a cualquier reclamación, sanción, daño o gasto (incluidos honorarios de abogados) que se derive del uso que hagas de la aplicación o del incumplimiento de este aviso o de la ley.

## 7. Limitación de responsabilidad

Si, pese a lo anterior, una ley aplicable no permitiera excluir totalmente la responsabilidad, esta quedará limitada al mínimo legalmente permitido. Dado que la aplicación es gratuita, ese límite no superará en ningún caso el importe que hayas pagado por ella (cero). Nada de este aviso excluye derechos que, como consumidor, no puedan excluirse según tu ley local.

## 8. Componentes y canales de terceros

La aplicación incorpora software de terceros (Whisper.cpp / Whisper.net, Silero VAD, NAudio, OnnxRuntime, SQLite, Velopack) con sus propias licencias, y se distribuye a través de servicios de terceros (GitHub Releases y Scoop). El autor no ejerce control sobre ellos ni ofrece garantía adicional. Los modelos de transcripción se descargan de sus fuentes oficiales; el modelo VAD se verifica por SHA256.

Los ejecutables **no están firmados con un certificado de firma de código**, por lo que Windows SmartScreen puede mostrar «Windows protegió su PC» al ejecutar el instalador, y Smart App Control (si está activado) puede bloquearlo. Descarga la aplicación solo desde el repositorio oficial y comprueba el SHA256 publicado en cada release (`SHA256SUMS.txt`).

## 9. Cambios en este aviso

Este aviso puede actualizarse. Cuando cambie de forma relevante, la aplicación volverá a pedir tu aceptación. La versión vigente es la publicada en el repositorio.

## 10. Contacto

Para cuestiones sobre este aviso, abre una incidencia en el repositorio: <https://github.com/josearias210/voice-recorder/issues>

---

## English (TL;DR)

By downloading, installing or using Voice Recorder you **accept this notice** and the [privacy policy](PRIVACY.md); the app asks for this acceptance on first launch and cannot be used without it. The software is provided **AS IS and AS AVAILABLE, without warranty of any kind** (MIT license). To the maximum extent permitted by law, the author and contributors **assume no liability** for any damage, data loss or consequence arising from its use or misuse. **You are solely responsible** for how you use it and you are the data controller of any transcript you create: recording or transcribing conversations may require the consent of all parties depending on your jurisdiction (GDPR, all-party consent laws, etc.). Covert recording or surveillance is not a permitted use, and the app is deliberately designed against it: it is always visible (window and tray icon showing the session state), only records when you press Start, never sends recordings anywhere on its own (data only leaves your PC when you choose it: transcribing with OpenAI sends the voice segments to the OpenAI API with your own key, and the optional, off-by-default backup you trigger manually copies to your own Azure Blob Storage account), and features for hidden or covert recording will not be accepted. You agree to indemnify the author against claims arising from your use. Automatic transcription contains errors and must not be relied upon as an accurate record. This notice is not legal advice.
