# Contribuir a Voice Recorder

¡Gracias por tu interés! Este proyecto aspira a ser open source y toda contribución es bienvenida.

## Cómo contribuir

1. **Abre un issue** antes de cambios grandes (features, refactors) para alinear el diseño.
2. Haz fork y crea una rama descriptiva: `feat/transcripcion-bilingue`, `fix/vad-ruido-fondo`, …
3. Asegúrate de que compila y pasa los tests. Con Docker (no requiere instalar .NET):

   ```powershell
   .\scripts\docker.ps1 all
   ```

   O con el SDK de .NET 10 instalado:

   ```powershell
   dotnet build VoiceRecorder.slnx
   dotnet test
   ```

   Si añades tests que no usan WPF/WASAPI/NAudio, inclúyelos también en `tests/VoiceRecorder.Tests.Portable/VoiceRecorder.Tests.Portable.csproj` para que corran en Docker y en el CI de Linux.

4. Envía un Pull Request describiendo el *qué* y el *por qué*.

## Funciones que no se aceptan

Voice Recorder es una herramienta para que **un participante transcriba su propia llamada con conocimiento de los demás**. Para mantener ese propósito (ver [DISCLAIMER.md](DISCLAIMER.md#diseño-transparente-lo-que-la-aplicación-no-hace-ni-hará)), **no se aceptarán issues ni PRs** que añadan:

- Modos ocultos, sin ventana o sin icono en la bandeja, o que escondan el estado de grabación.
- Grabación que empiece sola, en segundo plano o al arrancar Windows sin una acción explícita del usuario.
- Envío, subida o sincronización de grabaciones o transcripciones a servidores o a terceros.
- Captura de dispositivos, cuentas o comunicaciones en las que el usuario no participa.

Las issues que pidan ayuda para un uso no permitido (grabar a alguien sin que lo sepa, vigilar a otra persona…) se cerrarán sin respuesta técnica.

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

## Automatizaciones del repositorio

| Workflow | Cuándo | Qué hace |
|----------|--------|----------|
| `CI` | push y PR a `main` | Escaneo de secretos, build + tests completos en Windows, portable de prueba |
| `Linux checks` | push y PR a `main` | Pruebas portables, formato, actionlint y build de la imagen Docker de desarrollo |
| `CodeQL` | push, PR y semanal | Análisis de seguridad del código C# |
| `Dependency review` | PR | Bloquea dependencias nuevas con vulnerabilidades conocidas |
| `Release` | etiqueta `vX.Y.Z` | Instalador, portable, paquetes de actualización, GitHub Release y Scoop |
| Dependabot | semanal | PRs para actualizar paquetes NuGet, acciones y la imagen Docker |

## Reportar bugs

Incluye: versión de Windows, app de llamadas usada, modelo Whisper, pasos para reproducir y el texto de `StatusText` de la app si aplica.

## Ideas y roadmap

Consulta el roadmap en el [README](README.md#️-roadmap) y la pestaña de issues. Las ideas marcadas como `good first issue` son buen punto de entrada.
