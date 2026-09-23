# Contribuir a Voice Recorder

¡Gracias por tu interés! Este proyecto aspira a ser open source y toda contribución es bienvenida.

## Cómo contribuir

1. **Abre un issue** antes de cambios grandes (features, refactors) para alinear el diseño.
2. Haz fork y crea una rama descriptiva: `feat/transcripcion-bilingue`, `fix/vad-ruido-fondo`, …
3. Asegúrate de que compila y pasa los tests:

   ```powershell
   dotnet build VoiceRecorder.slnx
   dotnet test
   ```

4. Envía un Pull Request describiendo el *qué* y el *por qué*.

## Convenciones

- **Idioma**: UI y docs en español; identificadores y comentarios de código en inglés.
- **Estilo**: el repositorio incluye `.editorconfig` (file-scoped namespaces, braces siempre). El build no debe producir warnings nuevos.
- **Arquitectura**:

  | Proyecto | Responsabilidad | No debe… |
  |----------|-----------------|----------|
  | `Core` | DTOs e interfaces | referenciar nada |
  | `Audio` | captura/resampleo WASAPI | conocer Whisper |
  | `Transcription` | VAD + motor + orquestador | tocar UI o SQL |
  | `Storage` | SQLite | conocer el pipeline |
  | `App` | UI WPF + composición | implementar lógica de dominio |

- **Commits**: [Conventional Commits](https://www.conventionalcommits.org/es/) (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`).
- **Tests**: la lógica nueva (segmentación, exportación, persistencia) debe llevar tests unitarios. Los verificadores de hardware (`tools/*`) no se ejecutan en CI por requerir dispositivos de audio.

## Reportar bugs

Incluye: versión de Windows, app de llamadas usada, modelo Whisper, pasos para reproducir y el texto de `StatusText` de la app si aplica.

## Ideas y roadmap

Consulta el roadmap en el [README](README.md#️-roadmap) y la pestaña de issues. Las ideas marcadas como `good first issue` son buen punto de entrada.
