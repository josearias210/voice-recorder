<div align="center">

# 🎙️ Voice Recorder

**Transcribe tus llamadas en tiempo real. Cualquier aplicación. En local.**

Meet · Teams · Zoom · WhatsApp Web · Discord — si suena en tu PC, se transcribe.

[![CI](https://github.com/josearias210/voice-recorder/actions/workflows/ci.yml/badge.svg)](../../actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/josearias210/voice-recorder)](../../releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

---

**[English below](#english)** · Aplicación de escritorio para **Windows** que graba y transcribe las conversaciones de **cualquier app de llamadas**, sin drivers virtuales. Graba tu voz y la de la otra persona por separado y, al terminar, transcribe con **Whisper en tu equipo** (por defecto) o, si quieres, con **OpenAI** usando tu propia clave.

> ⚖️ **Aviso legal**: software gratuito ofrecido **«tal cual», sin garantías**. El autor **no se hace responsable** del uso que se haga de la aplicación ni de sus consecuencias: **cada usuario es el único responsable** de cumplir la ley y de obtener el consentimiento de las personas que participan en una conversación antes de grabarla o transcribirla. Está diseñada para que **un participante transcriba su propia llamada**: siempre es visible, solo graba cuando pulsas *Iniciar* y no envía nada por su cuenta (solo si tú eliges OpenAI o el backup); **no se admite su uso para grabar a escondidas o vigilar a otras personas**. La app pide aceptar este aviso al abrirse por primera vez. Lee el [aviso legal completo](DISCLAIMER.md) y la [política de privacidad](PRIVACY.md).

## ✨ Características

- 🔌 **Universal**: captura el audio a nivel de sistema (WASAPI Loopback + micrófono). No depende de Meet, Teams, Zoom ni de ninguna app concreta.
- 👥 **¿Quién dijo qué?**: burbujas `Yo` / `Ellos` separadas por detección de voz (VAD Silero) en cada canal.
- 🔒 **Local por defecto**: transcripción con Whisper (whisper.cpp) en tu máquina. Nada sale de tu PC salvo que elijas OpenAI o el backup opcional.
- 🧠 **Dos motores**: Whisper local (gratis) u OpenAI `gpt-transcribe` (de pago, con tu clave). Eliges el motor por defecto en **Configuración › Transcripción…** y puedes cambiarlo cada vez que transcribes.
- 🔁 **Transcribe cuando quieras y regenera**: al detener la grabación la app pregunta si transcribir ahora; desde el historial (botón **Historial**), **Transcribir…** vuelve a transcribir el audio guardado con el motor que elijas.
- ⏸ **Pausar y reanudar**: el cronómetro de sesión excluye las pausas.
- 💾 **Historial con etiquetas**: sesiones guardadas como carpetas (`Sesiones/AAAA/MM/…`), reabribles como vista de chat, con backup manual opcional en tu Azure Blob Storage.
- 📤 **Exportar**: TXT, Markdown y SRT (subtítulos).
- **Transcribir archivos**: importa audio o vídeo (mp3, wav, m4a, mp4, mkv, mov, …) arrastrándolos a la ventana o con el botón *Transcribir archivo*; cada archivo se guarda como una sesión del historial.
- 🖥️ **Bandeja del sistema**: control Iniciar/Pausar/Detener sin abrir la ventana.
- 🔄 **Auto-actualización**: con deltas incrementales vía [Velopack](https://github.com/velopack/velopack).
- 🎚️ **Audio por canal**: cada sesión guarda dos WAV, el del micrófono (tu voz, `Yo`) y el de la llamada (la otra persona, `Ellos`).
- 📝 **Notas de la sesión**: añade notas libres en el chat de cada sesión y guárdalas con título y etiquetas.

## 🚀 Instalación

### Instalador con auto-actualización (recomendado)

Descarga `VoiceRecorder-win-Setup.exe` desde [Releases](../../releases/latest) y ejecútalo.

La app **se actualiza sola**: al arrancar comprueba si hay una versión nueva y te ofrece *Actualizar y reiniciar* (descarga incremental, sin reinstalar). También puedes pulsar **Buscar actualizaciones** en **Configuración** (⚙, arriba a la derecha), donde se muestra la versión instalada (por ejemplo, `v1.3.0 · instalada`).

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

Requisitos: [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) y Windows. El instalador y el portable ya incluyen el runtime .NET 10.

## 📖 Uso

1. Elige tu micrófono, el modelo y el idioma en **Configuración** (⚙, arriba a la derecha; solo la primera vez). Entra a tu llamada (Meet, Teams, Zoom, …) y pulsa **Iniciar**. Arranca al instante y graba dos WAV: `grabacion-<fecha>-micro.wav` (tu voz) y `grabacion-<fecha>-llamada.wav` (la otra persona).
2. **Pausar** si necesitas un descanso · **Detener** para guardar la sesión con su audio.
3. Al detener, la app pregunta si quieres **transcribir ahora** y con qué motor (propone el de por defecto). Si eliges *Más tarde*, la sesión queda «sin transcribir».
4. Pulsa **Historial** (arriba a la derecha, o `Ctrl+H`) para abrir el panel de sesiones; allí **Transcribir…** transcribe la sesión seleccionada o **regenera** su transcripción (por ejemplo, con otro motor o modelo). La transcripción anterior se reemplaza solo cuando termina la nueva.
5. Abre la sesión para verla como chat (`Yo` a la derecha, `Ellos` a la izquierda), editar título y etiquetas o **exportar** (TXT/MD/SRT).

El audio ocupa unos 115 MB por hora y canal. El botón **Carpeta** del historial abre la carpeta de la sesión, y al eliminar la sesión se borra también su audio.

> 💡 **Consejo**: usa auriculares. Con altavoces, tu micrófono capta la voz remota y pueden aparecer burbujas `Yo` duplicadas.

### Motores de transcripción

En **Configuración › Transcripción…** eliges el motor por defecto:

| Motor | Dónde se ejecuta | Coste | Notas |
|-------|------------------|-------|-------|
| **Whisper local** (por defecto) | En tu equipo | Gratis | Modelo e idioma en **Configuración**. Con Large v3 Turbo es muy preciso, pero tarda más en CPU. |
| **OpenAI** (`gpt-transcribe`) | En la nube de OpenAI | ≈ 0,0045 USD por minuto de voz | Necesita tu clave de la API (se guarda cifrada con DPAPI). *Probar clave* la comprueba sin enviar audio. |

Con OpenAI, la detección de voz sigue siendo local: solo se envían los tramos con voz, en bloques de hasta 28 s y cada uno con su canal. Así se conservan `Yo`/`Ellos` y las marcas de tiempo, que `gpt-transcribe` no devuelve. Al terminar, la barra de estado muestra el tiempo que tardó y el coste estimado.

### Costes

**Costes…** (en **Configuración**) muestra cuánto llevas gastado este mes, el total y el tiempo dedicado a transcribir, con el detalle de cada transcripción: fecha, sesión, motor y modelo, minutos de audio, tiempo y coste. Cada sesión del historial muestra además su última transcripción, por ejemplo `OpenAI · 3 min 20 s · ≈ 0,0140 USD`.

- **Whisper local** cuesta 0: solo se registra el tiempo.
- **OpenAI**: coste estimado = minutos de voz enviados × precio por minuto. El precio (0,0045 USD por defecto) se puede ajustar en **Transcripción…**. Si una transcripción falla o se cancela a medias, lo ya enviado también se registra, porque OpenAI lo cobra igual. El importe real es el de tu panel de OpenAI (botón *Ver uso real en OpenAI*).
- El registro (`%LOCALAPPDATA%VoiceRecorder	ranscripciones.jsonl`) se conserva aunque borres sesiones.

### Transcribir archivos de audio o vídeo

Arrastra uno o varios archivos a la ventana, o pulsa **Transcribir archivo…**. La app pregunta con qué motor transcribirlos (Whisper local u OpenAI); cada uno se guarda como una sesión del historial (con su ruta en las notas) y puedes exportarla igual que cualquier otra (TXT/MD/SRT).

- Formatos admitidos: mp3, wav, m4a, mp4, mkv, mov, avi, wma, aac, flac, ogg, opus, webm (según códecs del sistema).
- La barra de progreso muestra el avance de cada archivo; puedes interrumpir con **Cancelar** (no se guarda nada a medias).

### Modelos de transcripción

| Modelo | Tamaño | Uso recomendado |
|--------|--------|-----------------|
| Tiny | ~30 MB | Máquinas muy limitadas |
| **Base (Q5)** | **~60 MB** | **Por defecto; rápido** |
| Base sin cuantizar | ~145 MB | Mejor calidad en CPU potente |
| Small (Q5) | ~180 MB | Más preciso |
| Large v3 Turbo (Q5) | ~575 MB | Máxima precisión; en CPU tarda bastante más |

Se eligen y descargan en **Configuración**. Los modelos se almacenan en `%LOCALAPPDATA%\VoiceRecorder\models`.

Whisper trabaja en modo preciso: junta las frases cercanas en bloques de hasta 28 s, usa el texto anterior como contexto y compara varias alternativas (búsqueda por haz). Así comete bastantes menos errores que con frases sueltas.

### Dónde se guardan las sesiones y backup externo

Cada sesión es una carpeta, sin base de datos:

```text
%LOCALAPPDATA%\VoiceRecorder\Sesiones\
└── 2026\11\20261119-093005-00012\
    ├── session.json            ← título, fecha, etiquetas, notas y transcripción
    ├── grabacion-…-micro.wav   ← tu voz (Yo)
    └── grabacion-…-llamada.wav ← la otra persona (Ellos)
```

Si venías de una versión anterior, la antigua `sessions.db` se migra automáticamente al arrancar (se renombra a `sessions.db.migrated`, no se borra).

**Backup externo (Azure Blob Storage)**: por defecto todo se queda en local. En **Configuración › Backup externo…** activa *Usar backup en Azure Blob Storage* e indica la cuenta de almacenamiento, el contenedor (se crea si no existe; por defecto `voice-recorder`) y la *access key* de la cuenta (o una cadena de conexión). La clave se guarda cifrada con DPAPI para tu usuario de Windows.

El backup es **siempre manual**: la app nunca sube ni baja nada por su cuenta. Pulsa **Sincronizar…** (en **Configuración**) y verás la comparación entre local y la nube, archivo por archivo (MD5):

- **Sincronizado al N %** y cuatro contadores: ✓ sincronizados (iguales en ambos lados), ⬆ en local y no arriba (falta subir), ⬇ arriba y no en local (falta bajar) y ≠ en ambos con contenido distinto.
- La lista de sesiones con el estado de cada una (filtrable); pasando el ratón se ve el estado de cada archivo.
- **⬆ Subir marcadas** sube lo que falta y reemplaza en la nube los archivos con el mismo nombre que cambiaron; los idénticos no se vuelven a subir.
- **⬇ Bajar marcadas** descarga a este equipo lo que falta (p. ej. al usar la app en otro ordenador), con la misma estructura de carpetas; *sobrescribir archivos locales distintos* reemplaza también los que hayan cambiado. Las sesiones descargadas aparecen en el historial al momento.
- Nunca se borra nada en la nube: borrar una sesión en local **no** la borra del backup.

## ⚙️ Cómo funciona

```
[WASAPI Loopback ─ voz de los demás]─► 16 kHz mono ─► llamada.wav ─┐
[Micrófono ─ tu voz]─────────────────► 16 kHz mono ─► micro.wav ───┤  (al detener o desde el historial)
                                                                    ▼
                    VAD Silero ─► bloques de voz por canal (Yo/Ellos) ─► Whisper local | OpenAI ─► Carpetas (sesiones) ─► UI (WPF)
```

- **WASAPI Loopback**: función nativa de Windows que entrega una copia del audio que el sistema reproduce. No intercepta ni modifica nada, no instala drivers y funciona con cualquier aplicación.
- **VAD Silero (ONNX)**: detecta voz real y descarta silencios; canales separados → atribución de hablante sin diarización compleja.
- **Whisper local**: transcripción con [Whisper.net](https://github.com/sandrohanea/whisper.net) (whisper.cpp).
- **Carpetas**: cada sesión es una carpeta con `session.json` (y sus WAV si grabas audio primero); `sessions.db` antiguo se migra solo.

### Estructura del repositorio

```
src/VoiceRecorder.Core          · DTOs e interfaces
src/VoiceRecorder.Audio         · Captura WASAPI dual + resampleo 16 kHz
src/VoiceRecorder.Transcription · VAD + Whisper + orquestador de sesión
src/VoiceRecorder.Storage       · Persistencia en carpetas + backup Azure
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
.\scripts\docker.ps1 test      # pruebas multiplataforma (núcleo, transcripción, almacenamiento)
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

- El audio de las llamadas se graba y, por defecto, se transcribe **en tu equipo** (Whisper + Silero VAD locales).
- No hay telemetría, analytics ni informes de fallos. Por su cuenta, la app solo descarga los modelos que tú pides y comprueba si hay actualizaciones en GitHub Releases.
- Las grabaciones y transcripciones se guardan en tu disco (`%LOCALAPPDATA%\VoiceRecorder\Sesiones`). Solo salen de él si eliges transcribir con **OpenAI** (los tramos con voz se envían a OpenAI con tu clave) o si haces un backup a tu propia cuenta de Azure.
- Detalles completos en [PRIVACY.md](PRIVACY.md).
- Respeta la legislación y el consentimiento de tu jurisdicción antes de grabar conversaciones — lee el [aviso legal](DISCLAIMER.md).

## 📄 Licencia

[MIT](LICENSE) — listo para uso libre. Las contribuciones se publican bajo la misma licencia. El software se ofrece «tal cual», sin garantías: el uso es bajo tu responsabilidad ([aviso legal](DISCLAIMER.md)).

---

<a name="english"></a>

## English (TL;DR)

Windows desktop app that records and transcribes **any call app** (Meet, Teams, Zoom, …). It captures system audio with WASAPI loopback plus the microphone, without virtual audio drivers, and saves your voice and the other party's voice as separate WAV files. When you stop, it asks whether to transcribe. You can use **local Whisper** (the default, no cloud) or the **OpenAI API** (`gpt-transcribe`, paid, with your own DPAPI-encrypted key). **Silero VAD** keeps speaker attribution (`Me` / `Them`), and you can regenerate a transcript later from the history.

- **Install**: grab the setup or portable ZIP from [Releases](../../releases).
- **Import & transcribe files**: drag audio/video files (mp3, mp4, m4a, mkv, …) onto the window — each one is saved as a session, fully local.
- **Build**: `dotnet build VoiceRecorder.slnx` (needs .NET 10 SDK, Windows; targets .NET 10).
- **Test**: `dotnet test`.
- **License**: MIT.

> ⚠️ Recording conversations may require the consent of all parties depending on your jurisdiction. The app is built for a participant transcribing their own call: it is always visible, records only when you press Start and never sends recordings anywhere on its own. Data leaves your PC only when you choose it: OpenAI transcription, or a manual backup to your own Azure account. Covert recording or surveillance is not a permitted use.
>
> 🛡️ The installer is not code-signed yet, so Windows SmartScreen may warn you (*More info → Run anyway*), or install with Scoop. Verify downloads against `SHA256SUMS.txt`.
