# 🔒 Política de privacidad — Voice Recorder

> Última actualización: 2026-11-23 · Aplica a partir de la versión 1.4.0

**En una frase:** Voice Recorder graba y guarda el audio **en tu equipo** y por defecto también lo transcribe ahí; el audio y el texto solo salen de tu disco cuando tú eliges transcribir con OpenAI o hacer un backup a tu propia cuenta de Azure.

## 1. Qué hace la aplicación

Voice Recorder captura el audio del **micrófono** y del **audio que tu sistema reproduce** (WASAPI Loopback) durante una llamada y guarda cada canal en un WAV. Cuando tú lo decides (al detener la grabación o más tarde desde el historial), detecta los turnos de voz con un modelo VAD (Silero, en tu equipo) y los transcribe con el motor que elijas:

- **Whisper (whisper.cpp)**, por defecto: se ejecuta en tu CPU. **No interviene ninguna nube ni servicio remoto.**
- **OpenAI** (opcional, de pago): los tramos con voz se envían a la API de OpenAI con tu propia clave. Ver el punto 3.

## 2. Qué se guarda en tu disco

Todo se almacena localmente en tu perfil de usuario (`%LOCALAPPDATA%\VoiceRecorder`):

| Ruta | Contenido | Cuándo |
|------|-----------|--------|
| `Sesiones\AAAA\MM\<fecha>-<id>\` | Una carpeta por sesión: `session.json` (título, fecha, duración, etiquetas, notas y los **textos** transcritos con sus marcas de tiempo) y sus WAV (micrófono y llamada). | Al pulsar *Detener* o al importar archivos. La transcripción se añade o se reemplaza cuando transcribes. (La antigua `sessions.db` se migra una vez y se renombra a `sessions.db.migrated`.) |
| `settings.json` | Preferencias: modelo, idioma, micrófono seleccionado, motor de transcripción por defecto, la versión del aviso legal que aceptaste y, si los configuras, la clave de OpenAI y el backup externo (cuenta y contenedor). Las claves se guardan **cifradas con DPAPI** para tu usuario de Windows. | Siempre que cambias un ajuste o aceptas el aviso. |
| `models\` | Los modelos de transcripción y VAD que descargues. | Al descargarlos desde la app. |
| `transcripciones.jsonl` | Registro de cada transcripción: fecha, sesión, motor y modelo, tiempo que tardó, minutos de audio y coste estimado (para «Costes…»). No contiene audio ni texto transcrito. | Cada vez que transcribes. |
| `audio\` | WAV temporales del micrófono y de la llamada mientras se graba. | Durante cada grabación. Al guardar la sesión se mueven a su carpeta en `Sesiones\` y sus rutas quedan en las notas. |

Desde la versión 1.4.0 **el audio siempre se guarda** (ya no existe la transcripción en vivo): así puedes transcribirlo cuando quieras y regenerar la transcripción. Ocupa unos 115 MB por hora y canal; bórralo eliminando la sesión.

## 3. Cuándo sale información de tu equipo

Solo en estos casos. Los tres primeros nunca incluyen contenido de tus conversaciones; el cuarto y el quinto sí, y solo cuando tú lo eliges:

1. **Descarga de modelos** (la inicias tú): el modelo Whisper se descarga desde Hugging Face y el modelo Silero VAD desde GitHub (versión fijada y verificada por SHA256). Se hace una única vez por modelo.
2. **Comprobación de actualizaciones**: si instalaste la app con el instalador, unos segundos tras arrancar (o cuando pulsas *Buscar actualizaciones*) se consulta GitHub Releases para saber si hay una versión nueva (petición HTTPS estándar: tu IP y cabeceras habituales, sujeta a la [política de privacidad de GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement)). Solo se descarga una actualización si pulsas *Actualizar y reiniciar*. Las builds portable no comprueban actualizaciones.
3. **Instalación vía gestores de paquetes**: Scoop descarga los instalables desde GitHub Releases.
4. **Backup externo (opcional, desactivado por defecto)**: si lo activas en *Backup externo…* con tu propia cuenta de Azure Blob Storage, la app sube por HTTPS a **tu** contenedor, con la misma estructura, las sesiones (transcripciones, notas y WAV) que marques en *☁ Sincronizar…*, y baja las que elijas. Nunca lo hace por su cuenta: solo cuando lo pides. Solo añade o actualiza archivos: borrar una sesión en local no la borra del backup. Esos datos quedan sujetos a la configuración y la [política de privacidad de Microsoft Azure](https://privacy.microsoft.com/privacystatement).
5. **Transcripción con OpenAI (opcional, de pago)**: solo si guardas tu clave en *Transcripción…* y eliges OpenAI al transcribir. La app envía por HTTPS a `api.openai.com` cada bloque de voz (hasta 28 s, en WAV) de los canales de la sesión, el idioma y, como contexto, el final del texto ya transcrito de ese mismo hablante. Los silencios no se envían. El uso se factura en tu cuenta y esos datos quedan sujetos a las condiciones y la [política de privacidad de OpenAI](https://openai.com/policies/privacy-policy/). La clave se guarda cifrada con DPAPI; *Probar clave* solo consulta el modelo, sin enviar audio.

El autor de la aplicación **no opera ningún servidor** y no recibe ningún dato tuyo: estas conexiones van directamente a GitHub, Hugging Face, OpenAI o tu cuenta de Azure.

La aplicación **no incluye telemetría, analytics, informes de fallos, publicidad ni rastreadores** de ningún tipo. Salvo la transcripción con OpenAI y el backup externo, que tú eliges, ninguna parte de tus transcripciones, notas, audio ni metadatos se envía a ningún servidor.

## 4. Permisos que utiliza

- **Micrófono**: para capturar tu voz durante una sesión de grabación. Solo se captura mientras la sesión está activa (Iniciar → Detener/Pausar).
- **Loopback del sistema (WASAPI)**: para capturar el audio que Windows reproduce (la voz de los participantes remotos). No accede al contenido de otras ventanas ni aplicaciones.

## 5. Cómo eliminar tus datos

- **Sesiones concretas**: botón *Eliminar sesión* en la app (borra la carpeta de la sesión en local; las copias del backup externo se borran desde tu cuenta de Azure).
- **Grabaciones**: están en la carpeta de cada sesión (botón *Carpeta* del historial).
- **Todo de golpe**: desinstala la app y borra la carpeta `%LOCALAPPDATA%\VoiceRecorder`. No se guarda nada fuera de esa carpeta, salvo el backup externo si lo activaste y el audio enviado a OpenAI si lo usaste (consulta en tu cuenta de OpenAI cómo gestionan y borran esos datos).

## 6. Quién es responsable de los datos

Las transcripciones, notas y grabaciones se guardan **en tu equipo**. Si contienen datos de otras personas, **tú eres el responsable de su tratamiento** (por ejemplo, a efectos del RGPD) y de contar con su consentimiento o una base legal. El autor no tiene acceso a ellos. Ver [DISCLAIMER.md](DISCLAIMER.md).

## 7. Componentes de terceros

La app usa bibliotecas open source que se ejecutan localmente: Whisper.net (whisper.cpp), Microsoft ML OnnxRuntime (Silero VAD), NAudio (WASAPI), Microsoft.Data.Sqlite (migración de datos antiguos), Azure.Storage.Blobs (backup externo opcional) y Velopack (auto-actualización). La transcripción con OpenAI usa la API HTTPS de OpenAI directamente, sin SDK de terceros. Sus licencias están en los paquetes NuGet correspondientes. Ver [DISCLAIMER.md](DISCLAIMER.md) para la exención de responsabilidad.

---

## English (TL;DR)

Voice Recorder always records each call to WAV (microphone and call audio separately) **on your machine**. When you choose, it transcribes the audio locally with Whisper (the default, no cloud) or, optionally, with the paid OpenAI API using your own key. With OpenAI, the voice segments are sent to OpenAI, and the key is stored encrypted with DPAPI. Transcripts, audio, notes and settings live in `%LOCALAPPDATA%\VoiceRecorder`. Sessions are stored as folders (`Sesiones\YYYY\MM\...`); an optional, off-by-default external backup copies them to your own Azure Blob Storage container (key encrypted with DPAPI). The app has **no telemetry, analytics or crash reporting**; the only other network traffic is model downloads you trigger and an update check against GitHub Releases (installed builds). Delete everything by uninstalling and removing that folder. Recording conversations may require the consent of all parties — see [DISCLAIMER.md](DISCLAIMER.md).
