# Política de Seguridad

## Versiones soportadas

Solo la última versión publicada recibe correcciones de seguridad. Las instalaciones con Setup se actualizan solas.

| Versión | Soporte |
|---------|---------|
| 1.6.x   | ✅      |
| < 1.6   | ❌      |

## Reportar una vulnerabilidad

Si descubres una vulnerabilidad de seguridad, **por favor no abras un issue público**.

1. Usa [GitHub Security Advisories](../../security/advisories/new) ("Report a vulnerability") para un reporte privado.

Incluye: descripción, pasos para reproducir, impacto estimado y, si es posible, una prueba de concepto. Se intentará responder en un plazo razonable y publicar un parche y un advisory coordinado, sin que ello suponga un compromiso de soporte (ver [DISCLAIMER.md](DISCLAIMER.md)).

## Alcance y consideraciones de diseño

Voice Recorder procesa audio de llamadas en local. Puntos sensibles del diseño:

- **Sin telemetría ni servidores propios**: la app solo usa la red para (1) descargar modelos cuando el usuario lo pide (Whisper desde Hugging Face; Silero VAD desde GitHub, fijado a la etiqueta `v5.1.2` y verificado por SHA256), (2) consultar y descargar actualizaciones desde GitHub Releases en las instalaciones con Setup, (3) el backup externo opcional a la cuenta de Azure Blob Storage del propio usuario, solo cuando él lo pide (HTTPS; la clave se guarda cifrada con DPAPI) y (4) la transcripción opcional con la API de OpenAI (`api.openai.com`, HTTPS), solo cuando el usuario la elige para una sesión o archivo concretos. Envía los tramos con voz con la clave del propio usuario, guardada cifrada con DPAPI (entropía distinta a la del backup) y nunca escrita en registros ni en mensajes de error.
- **Sin secretos en el repositorio**: el código no contiene claves, tokens ni credenciales. El workflow de publicación solo usa el `GITHUB_TOKEN` efímero que proporciona GitHub Actions. Hay varias barreras, de la más cercana al desarrollador a la publicación:
  1. **Hook `pre-commit`** (`.githooks/pre-commit`): revisa con [gitleaks](https://github.com/gitleaks/gitleaks) los cambios preparados antes de cada commit. Se activa una vez por clon con `.\scripts\docker.ps1 hooks` (solo requiere Docker).
  2. **Push protection de GitHub**: rechaza un `git push` que contenga un secreto reconocido de un proveedor (GitHub, AWS, Azure, Google…).
  3. **CI** (`ci.yml`): gitleaks revisa **todo el historial** en cada push y PR; un hallazgo hace fallar el PR y se publica en *Security → Code scanning*.
  4. **Release** (`release.yml`): no se genera ningún paquete si el historial etiquetado contiene un secreto, y además se revisan los archivos que se van a distribuir antes de empaquetarlos.
  5. **Secret scanning de GitHub**: avisa de cualquier secreto de proveedor que aparezca en el repositorio.

  Si un secreto llega a subirse, **revócalo y genera uno nuevo de inmediato**: borrarlo en un commit posterior no basta, porque sigue en el historial y en los clones existentes.
- **Datos locales**: transcripciones y configuración viven en `%LOCALAPPDATA%\VoiceRecorder` sin cifrar; cualquiera con acceso a tu perfil de Windows puede leerlas. Protege tu cuenta y tu disco (por ejemplo, con BitLocker). Reporta cualquier mecanismo que permita exfiltrarlas o ejecutar código con permisos del usuario.
- **Integridad de los paquetes**: cada release publica `SHA256SUMS.txt` y GitHub muestra el digest SHA256 de cada archivo; compruébalo antes de instalar (`Get-FileHash VoiceRecorder-win-Setup.exe`). El instalador aún no está firmado con certificado de código, por lo que Windows SmartScreen puede advertir al ejecutarlo.
