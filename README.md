<div align="center">

# 🎙️ Voice Recorder

**Transcribe tus llamadas en tiempo real. Cualquier aplicación. En local.**

Meet · Teams · Zoom · WhatsApp Web · Discord — si suena en tu PC, se transcribe.

[![CI](https://github.com/josearias210/voice-recorder/actions/workflows/ci.yml/badge.svg)](../../actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/josearias210/voice-recorder)](../../releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

---

**[English below](#english)** · Aplicación de escritorio para **Windows** que transcribe **en vivo** las conversaciones de **cualquier app de llamadas**, sin drivers virtuales, sin nube y **sin guardar el audio**: solo la transcripción.

> ⚖️ **Aviso legal**: software gratuito ofrecido **«tal cual», sin garantías**. El autor **no se hace responsable** del uso que se haga de la aplicación ni de sus consecuencias: **cada usuario es el único responsable** de cumplir la ley y de obtener el consentimiento de las personas que participan en una conversación antes de grabarla o transcribirla. Está diseñada para que **un participante transcriba su propia llamada**: siempre es visible, solo graba cuando pulsas *Iniciar* y no envía nada a nadie; **no se admite su uso para grabar a escondidas o vigilar a otras personas**. La app pide aceptar este aviso al abrirse por primera vez. Lee el [aviso legal completo](DISCLAIMER.md) y la [política de privacidad](PRIVACY.md).

## ✨ Características

- 🔌 **Universal**: captura el audio a nivel de sistema (WASAPI Loopback + micrófono). No depende de Meet, Teams, Zoom ni de ninguna app concreta.
- 👥 **¿Quién dijo qué?**: burbujas `Yo` / `Ellos` separadas por detección de voz (VAD Silero) en cada canal.
- 🔒 **100% local**: transcripción con Whisper (whisper.cpp) en tu máquina. Nada sale de tu PC.
- ⏸ **Pausar y reanudar**: el cronómetro de sesión excluye las pausas.
- 💾 **Historial con etiquetas**: sesiones en SQLite, reabribles como vista de chat.
- 📤 **Exportar**: TXT, Markdown y SRT (subtítulos).
- **Transcribir archivos**: importa audio o vídeo (mp3, wav, m4a, mp4, mkv, mov, …) arrastrándolos a la ventana o con el botón *Transcribir archivo*; cada archivo se guarda como una sesión del historial.
- 🖥️ **Bandeja del sistema**: control Iniciar/Pausar/Detener sin abrir la ventana.
- 🔄 **Auto-actualización**: con deltas incrementales vía [Velopack](https://github.com/velopack/velopack).
- 🔇 **Sin audio guardado**: por defecto solo se conserva el texto (el audio vive en memoria).
- 📝 **Notas de la sesión**: añade notas libres en el chat de cada sesión y guárdalas con título y etiquetas.
- 🎚️ **Modo «grabar audio primero»**: opcional; graba el audio a WAV (micrófono y llamada por separado) sin transcribir en vivo y lo transcribe al pulsar Detener.

## 🚀 Instalación

### Instalador con auto-actualización (recomendado)

Descarga `VoiceRecorder-win-Setup.exe` desde [Releases](../../releases/latest) y ejecútalo.

La app **se actualiza sola**: al arrancar comprueba si hay una versión nueva y te ofrece *Actualizar y reiniciar* (descarga incremental, sin reinstalar). También puedes pulsar **Buscar actualizaciones** en la barra inferior, donde se muestra la versión instalada (por ejemplo, `v1.3.0 · instalada`).

> Requiere **Windows 10 2004+ / Windows 11** (64 bits). El modelo de transcripción se descarga dentro de la app (≈60 MB).

> 🛡️ **«Windows protegió su PC»**: el instalador aún no está firmado con un certificado de código, así que SmartScreen puede avisar al abrirlo. Si lo descargaste de este repositorio, pulsa **Más información → Ejecutar de todas formas**, o instala con Scoop, que no muestra ese aviso. Puedes comprobar el archivo con `Get-FileHash VoiceRecorder-win-Setup.exe` y compararlo con `SHA256SUMS.txt` del release. Si tienes **Smart App Control** activado, Windows bloqueará la app hasta que esté firmada.

### Gestores de paquetes

```powershell
# scoop (bucket de la comunidad del proyecto)
scoop bucket add josearias210 https://github.com/josearias210/voice-recorder
scoop install voice-recorder
```

> Los canales de instalación son GitHub Releases (Setup y portable) y Scoop. La auto-actualización integrada requiere una instalación con Setup; no está disponible en el portable ni en Scoop.

### Portable

Descarga `VoiceRecorder-win-Portable.zip` desde [Releases](../../releases/latest), descomprime y ejecuta `VoiceRecorder.exe`.

### Desde el código fuente

```powershell
git clone https://github.com/josearias210/voice-recorder.git
cd voice-recorder
dotnet build VoiceRecorder.slnx -c Release
dotnet run --project src/VoiceRecorder.App
```

Requisitos: [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) y Windows. El instalador y el portable ya incluyen el runtime .NET 8.

## 📖 Uso

1. Entra a tu llamada (Meet, Teams, Zoom, …) y pulsa **▶ Iniciar**.
2. La conversación aparece en vivo en la vista de chat (`Yo` a la derecha, `Ellos` a la izquierda).
3. **⏸ Pausar** si necesitas un descanso · **■ Detener** para guardar la sesión.
4. Abre la sesión desde el **Historial** para editar título/etiquetas o **exportar** (TXT/MD/SRT).

> 💡 **Consejo**: usa auriculares. Con altavoces, tu micrófono capta la voz remota y pueden aparecer burbujas `Yo` duplicadas.

### Modo «grabar audio primero»

Activa la casilla **Grabar audio y transcribir al detener** si tu equipo va justo o prefieres el texto al final:

1. Pulsa **▶ Iniciar**: arranca al instante (no carga modelos) y graba dos WAV por sesión — `grabacion-<fecha>-micro.wav` y `grabacion-<fecha>-llamada.wav` — en `%LOCALAPPDATA%\VoiceRecorder\audio`.
2. Pulsa **■ Detener**: se transcribe el audio grabado (con la atribución `Yo`/`Ellos` de cada canal) y la sesión se guarda con las rutas del audio en las notas.
3. El audio se conserva en disco (≈115 MB/hora por canal); puedes borrarlo cuando quieras desde esa carpeta. Si falla la transcripción, la sesión conserva las rutas para volver a importar los WAV.

### Transcribir archivos de audio o vídeo

Arrastra uno o varios archivos a la ventana, o pulsa **Transcribir archivo…**. Se transcriben en local con el modelo e idioma seleccionados, cada uno se guarda como una sesión del historial (con su ruta en las notas) y puedes exportarla igual que cualquier otra (TXT/MD/SRT).

- Formatos admitidos: mp3, wav, m4a, mp4, mkv, mov, avi, wma, aac, flac, ogg, opus, webm (según códecs del sistema).
- La barra de progreso muestra el avance de cada archivo; puedes interrumpir con **Cancelar** (no se guarda nada a medias).

### Modelos de transcripción

| Modelo | Tamaño | Uso recomendado |
|--------|--------|-----------------|
| Tiny | ~30 MB | Máquinas muy limitadas |
| **Base (Q5)** | **~60 MB** | **Recomendado por defecto** |
| Base sin cuantizar | ~145 MB | Mejor calidad en CPU potente |
| Small (Q5) | ~180 MB | Máxima precisión |

Los modelos se almacenan en `%LOCALAPPDATA%\VoiceRecorder\models`.

## ⚙️ Cómo funciona

```
[WASAPI Loopback ─ voz de los demás]──┐
                                      ├─► 16 kHz mono ─► VAD Silero ─► Enunciados etiquetados ─► Whisper ─► Chat en vivo
[Micrófono ─ tu voz]──────────────────┘    por canal       (Yo/Ellos)                              │
                                                                                                          ▼
                                              UI (WPF) ◄────────────────────────────────────── SQLite (sesiones)
```

- **WASAPI Loopback**: función nativa de Windows que entrega una copia del audio que el sistema reproduce. No intercepta ni modifica nada, no instala drivers y funciona con cualquier aplicación.
- **VAD Silero (ONNX)**: detecta voz real y descarta silencios; canales separados → atribución de hablante sin diarización compleja.
- **Whisper local**: transcripción con [Whisper.net](https://github.com/sandrohanea/whisper.net) (whisper.cpp).
- **SQLite**: historial de sesiones y segmentos.

### Estructura del repositorio

```
src/VoiceRecorder.Core          · DTOs e interfaces
src/VoiceRecorder.Audio         · Captura WASAPI dual + resampleo 16 kHz
src/VoiceRecorder.Transcription · VAD + Whisper + orquestador de sesión
src/VoiceRecorder.Storage       · Persistencia SQLite
src/VoiceRecorder.App           · UI WPF (chat, historial, bandeja, updates)
tools/CaptureVerifier           · Verifica captura de audio (Fase 1)
tools/PipelineVerifier          · Verificación E2E del pipeline
tests/VoiceRecorder.Tests       · Tests unitarios
bucket/                         · Manifest de scoop (se actualiza en cada release)
scripts/                        · Build/publish y tareas Docker
```

## 🗺️ Roadmap

- [x] Guardar audio opcional (WAV por sesión)
- [ ] Captura por proceso (ignorar notificaciones y música)
- [ ] Resúmenes automáticos con IA local (Ollama)
- [x] Búsqueda en títulos, etiquetas, notas y texto de las transcripciones
- [ ] Identificación de hablantes reales (diarización)

## 🛠️ Desarrollo

### Con Docker (sin instalar nada)

Solo necesitas [Docker Desktop](https://www.docker.com/products/docker-desktop/) en modo contenedores Linux. No hace falta instalar .NET:

```powershell
.\scripts\docker.ps1 test      # pruebas multiplataforma (núcleo, transcripción, SQLite)
.\scripts\docker.ps1 build     # compila toda la solución, incluida la app WPF, con -warnaserror
.\scripts\docker.ps1 run       # genera la app portable y la abre (artifacts\docker-publish\VoiceRecorder.exe)
.\scripts\docker.ps1 all       # build + test + formato + lint de workflows + escaneo de secretos
```

Otras tareas: `publish` (solo genera la app), `format`, `lint`, `secrets`, `shell` (terminal en el contenedor) y `clean`. Sin el script: `docker compose run --rm test`.

> La app es de Windows (WPF + WASAPI): se **compila** en Docker y se **ejecuta** en tu Windows desde la carpeta generada (autocontenida, sin instalar .NET). Las pruebas que dependen de WPF/WASAPI corren en el CI de Windows.

### Con el SDK de .NET

```powershell
dotnet build VoiceRecorder.slnx        # compilar
dotnet test                            # tests unitarios
dotnet run --project tools/CaptureVerifier -- 6     # probar captura de audio
dotnet run --project tools/PipelineVerifier -- 12 es   # E2E del pipeline (usa TTS)
.\scripts\build.ps1                    # publish + zip portable
.\scripts\build.ps1 -VpkVersion 0.9.2  # + paquetes Velopack para probar updates en local
```

#### Probar el flujo de actualización en local

1. `.\scripts\build.ps1 -VpkVersion 0.9.1` e instala `artifacts\vpk-test\VoiceRecorder-win-Setup.exe`
2. Genera una versión superior: `.\scripts\build.ps1 -VpkVersion 0.9.2`
3. Lanza la app apuntando a la carpeta local de paquetes:

   ```powershell
   $env:VR_UPDATE_SOURCE = "$PWD\artifacts\vpk-test"
   & "$env:LOCALAPPDATA\VoiceRecorder\VoiceRecorder.exe"
   ```

4. A los 5 segundos aparecerá el banner de actualización → *Actualizar y reiniciar*.

Ver [CONTRIBUTING.md](CONTRIBUTING.md) para convenciones y flujo de PRs.

## 🔒 Privacidad

- El audio de las llamadas se procesa **íntegramente en tu equipo** (Whisper + Silero VAD locales).
- No hay telemetría, analytics ni informes de fallos. Lo único que sale de tu equipo son las descargas de modelos que tú inicias y la comprobación de actualizaciones contra GitHub Releases.
- Las transcripciones se guardan en tu disco local (`%LOCALAPPDATA%\VoiceRecorder`); el audio solo se escribe a disco si activas el modo «Grabar audio primero».
- Detalles completos en [PRIVACY.md](PRIVACY.md).
- Respeta la legislación y el consentimiento de tu jurisdicción antes de grabar conversaciones — lee el [aviso legal](DISCLAIMER.md).

## 📄 Licencia

[MIT](LICENSE) — listo para uso libre. Las contribuciones se publican bajo la misma licencia. El software se ofrece «tal cual», sin garantías: el uso es bajo tu responsabilidad ([aviso legal](DISCLAIMER.md)).

---

<a name="english"></a>

## English (TL;DR)

Windows desktop app that transcribes **any call app** (Meet, Teams, Zoom, …) **in real time**, using system-level audio capture (WASAPI loopback + microphone), **Silero VAD** for speaker attribution (`Me` / `Them`) and **local Whisper** transcription — no cloud, no virtual audio drivers; audio is never saved by default (the optional *record-first* mode stores it locally).

- **Install**: grab the setup or portable ZIP from [Releases](../../releases).
- **Import & transcribe files**: drag audio/video files (mp3, mp4, m4a, mkv, …) onto the window — each one is saved as a session, fully local.
- **Build**: `dotnet build VoiceRecorder.slnx` (needs .NET 10 SDK, Windows; targets .NET 8).
- **Test**: `dotnet test`.
- **License**: MIT.

> ⚠️ Recording conversations may require the consent of all parties depending on your jurisdiction. The app is built for a participant transcribing their own call: it is always visible, records only when you press Start and never sends recordings anywhere. Covert recording or surveillance is not a permitted use.
>
> 🛡️ The installer is not code-signed yet, so Windows SmartScreen may warn you (*More info → Run anyway*), or install with Scoop. Verify downloads against `SHA256SUMS.txt`.
