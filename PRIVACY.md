# 🔒 Política de privacidad — Voice Recorder

> Última actualización: 14 de octubre de 2026 · Aplica a partir de la versión 1.2.0

**En una frase:** Voice Recorder procesa todo el audio **en tu equipo**; el texto y el audio nunca salen de tu disco salvo cuando tú decides descargar un modelo o aceptar una actualización.

## 1. Qué hace la aplicación

Voice Recorder captura el audio del **micrófono** y del **audio que tu sistema reproduce** (WASAPI Loopback) durante una llamada, detecta los turnos de voz con un modelo VAD (Silero) y los transcribe con **Whisper (whisper.cpp)** ejecutándose localmente en tu CPU. **No hay ninguna nube, API ni servicio remoto involucrado en la transcripción.**

## 2. Qué se guarda en tu disco

Todo se almacena localmente en tu perfil de usuario (`%LOCALAPPDATA%\VoiceRecorder`):

| Ruta | Contenido | Cuándo |
|------|-----------|--------|
| `sessions.db` | Sesiones guardadas: título, fecha, duración, etiquetas, notas y los **textos** transcritos con sus marcas de tiempo. | Al pulsar *Detener* o al importar archivos. |
| `settings.json` | Preferencias: modelo, idioma, micrófono seleccionado y modo de grabación. | Siempre que cambias un ajuste. |
| `models\` | Los modelos de transcripción y VAD que descargues. | Al descargarlos desde la app. |
| `audio\` | Grabaciones WAV del micrófono y de la llamada, **solo si activas el modo «Grabar audio y transcribir al detener»** (opcional, desactivado por defecto). | Solo en ese modo; las rutas quedan anotadas en las notas de la sesión. |

En el modo por defecto (transcripción en vivo) el audio **vive únicamente en memoria** y se descarta: nunca se escribe en disco.

## 3. Cuándo sale información de tu equipo

Solo en estos tres casos, y nunca con contenido de tus conversaciones:

1. **Descarga de modelos** (la inicias tú): el modelo Whisper se descarga desde Hugging Face y el modelo Silero VAD desde GitHub. Se hace una única vez por modelo.
2. **Comprobación de actualizaciones**: si instalaste la app con el instalador, unos segundos tras arrancar se consulta GitHub Releases para saber si hay una versión nueva (petición HTTPS estándar: tu IP y cabeceras habituales). Solo se descarga una actualización si pulsas *Actualizar*. Las builds portable no comprueban actualizaciones.
3. **Instalación vía gestores de paquetes**: winget o scoop descargan los instalables desde GitHub Releases.

La aplicación **no incluye telemetría, analytics, informes de fallos, publicidad ni rastreadores** de ningún tipo. Ninguna parte de tus transcripciones, notas, audio ni metadatos se envía a ningún servidor.

## 4. Permisos que utiliza

- **Micrófono**: para capturar tu voz durante una sesión de grabación. Solo se captura mientras la sesión está activa (Iniciar → Detener/Pausar).
- **Loopback del sistema (WASAPI)**: para capturar el audio que Windows reproduce (la voz de los participantes remotos). No accede al contenido de otras ventanas ni aplicaciones.

## 5. Cómo eliminar tus datos

- **Sesiones concretas**: botón *Eliminar sesión* en la app (borra textos y metadatos de la base de datos).
- **Grabaciones del modo «grabar primero»**: borra los archivos en `%LOCALAPPDATA%\VoiceRecorder\audio`.
- **Todo de golpe**: desinstala la app y borra la carpeta `%LOCALAPPDATA%\VoiceRecorder`. No se guarda nada fuera de esa carpeta.

## 6. Componentes de terceros

La app usa bibliotecas open source que se ejecutan localmente: Whisper.net (whisper.cpp), Microsoft ML OnnxRuntime (Silero VAD), NAudio (WASAPI), Microsoft.Data.Sqlite y Velopack (auto-actualización). Sus licencias están en los paquetes NuGet correspondientes. Ver [DISCLAIMER.md](DISCLAIMER.md) para la exención de responsabilidad.

---

## English (TL;DR)

Voice Recorder processes all audio **locally on your machine** (Whisper + Silero VAD, no cloud). Transcripts, notes and settings live in `%LOCALAPPDATA%\VoiceRecorder`. Audio is only written to disk if you enable the optional *record-first* mode. The app has **no telemetry, analytics or crash reporting**; the only network traffic is model downloads you trigger and an update check against GitHub Releases (installed builds). Delete everything by uninstalling and removing that folder. Recording conversations may require the consent of all parties — see [DISCLAIMER.md](DISCLAIMER.md).
