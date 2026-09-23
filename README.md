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

## ✨ Características

- 🔌 **Universal**: captura el audio a nivel de sistema (WASAPI Loopback + micrófono). No depende de Meet, Teams, Zoom ni de ninguna app concreta.
- 👥 **¿Quién dijo qué?**: burbujas `Yo` / `Ellos` separadas por detección de voz (VAD Silero) en cada canal.
- 🔒 **100% local**: transcripción con Whisper (whisper.cpp) en tu máquina. Nada sale de tu PC.
- ⏸ **Pausar y reanudar**: el cronómetro de sesión excluye las pausas.
- 💾 **Historial con etiquetas**: sesiones en SQLite, reabribles como vista de chat.
- 📤 **Exportar**: TXT, Markdown y SRT (subtítulos).
- 🖥️ **Bandeja del sistema**: control Iniciar/Pausar/Detener sin abrir la ventana.
- 🔇 **Sin audio guardado**: por defecto solo se conserva el texto (el audio vive en memoria).

## 🚀 Instalación

### Instalador (recomendado)

Descarga `VoiceRecorder-Setup-<versión>.exe` desde [Releases](../../releases) y ejecútalo.

> Requiere **Windows 10 2004+ / Windows 11** (64 bits). El modelo de transcripción se descarga dentro de la app (≈60 MB).

### Portable

Descarga `VoiceRecorder-portable-<versión>.zip`, descomprime y ejecuta `VoiceRecorder.exe`.

### Desde el código fuente

```powershell
git clone https://github.com/josearias210/voice-recorder.git
cd voice-recorder
dotnet build VoiceRecorder.slnx -c Release
dotnet run --project src/VoiceRecorder.App
```

Requisitos: [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0) y Windows.

## 📖 Uso

1. Entra a tu llamada (Meet, Teams, Zoom, …) y pulsa **▶ Iniciar**.
2. La conversación aparece en vivo en la vista de chat (`Yo` a la derecha, `Ellos` a la izquierda).
3. **⏸ Pausar** si necesitas un descanso · **■ Detener** para guardar la sesión.
4. Abre la sesión desde el **Historial** para editar título/etiquetas o **exportar** (TXT/MD/SRT).

> 💡 **Consejo**: usa auriculares. Con altavoces, tu micrófono capta la voz remota y pueden aparecer burbujas `Yo` duplicadas.

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
src/VoiceRecorder.App           · UI WPF (chat, historial, bandeja)
tools/CaptureVerifier           · Verifica captura de audio (Fase 1)
tools/PipelineVerifier          · Verificación E2E del pipeline
tests/VoiceRecorder.Tests       · Tests unitarios
installer/                      · Script de Inno Setup
scripts/                        · Scripts de build/publish
```

## 🗺️ Roadmap

- [ ] Guardar audio opcional (WAV/Opus por sesión)
- [ ] Captura por proceso (ignorar notificaciones y música)
- [ ] Resúmenes automáticos con IA local (Ollama)
- [ ] Búsqueda de texto completo en transcripciones
- [ ] Identificación de hablantes reales (diarización)

## 🛠️ Desarrollo

```powershell
dotnet build VoiceRecorder.slnx        # compilar
dotnet test                            # tests unitarios
dotnet run --project tools/CaptureVerifier -- 6     # probar captura de audio
dotnet run --project tools/PipelineVerifier -- 12 es   # E2E del pipeline (usa TTS)
.\scripts\build.ps1                    # publish + portable zip
.\scripts\build-installer.ps1          # instalador (requiere Inno Setup 6)
```

Ver [CONTRIBUTING.md](CONTRIBUTING.md) para convenciones y flujo de PRs.

## 🔒 Privacidad

- El audio de las llamadas se procesa **íntegramente en tu equipo**.
- No hay telemetría, analytics ni llamadas de red salvo la descarga de modelos que tú inicies.
- Las transcripciones se guardan en tu disco local (`%LOCALAPPDATA%\VoiceRecorder`).
- Respeta la legislación y consentimiento de tu jurisdicción antes de grabar conversaciones.

## 📄 Licencia

[MIT](LICENSE) — listo para uso libre. Las contribuciones se publican bajo la misma licencia.

---

<a name="english"></a>

## English (TL;DR)

Windows desktop app that transcribes **any call app** (Meet, Teams, Zoom, …) **in real time**, using system-level audio capture (WASAPI loopback + microphone), **Silero VAD** for speaker attribution (`Me` / `Them`) and **local Whisper** transcription — no cloud, no virtual audio drivers, audio is never saved.

- **Install**: grab the setup or portable ZIP from [Releases](../../releases).
- **Build**: `dotnet build VoiceRecorder.slnx` (needs .NET 8 SDK, Windows).
- **Test**: `dotnet test`.
- **License**: MIT.

> ⚠️ Recording conversations may require the consent of all parties depending on your jurisdiction.
