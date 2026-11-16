# Política de Seguridad

## Versiones soportadas

Solo la última versión publicada recibe correcciones de seguridad. Las instalaciones con Setup se actualizan solas.

| Versión | Soporte |
|---------|---------|
| 1.3.x   | ✅      |
| < 1.3   | ❌      |

## Reportar una vulnerabilidad

Si descubres una vulnerabilidad de seguridad, **por favor no abras un issue público**.

1. Usa [GitHub Security Advisories](../../security/advisories/new) ("Report a vulnerability") para un reporte privado.

Incluye: descripción, pasos para reproducir, impacto estimado y, si es posible, una prueba de concepto. Se intentará responder en un plazo razonable y publicar un parche y un advisory coordinado, sin que ello suponga un compromiso de soporte (ver [DISCLAIMER.md](DISCLAIMER.md)).

## Alcance y consideraciones de diseño

Voice Recorder procesa audio de llamadas en local. Puntos sensibles del diseño:

- **Sin telemetría ni servidores propios**: la app solo usa la red para (1) descargar modelos cuando el usuario lo pide (Whisper desde Hugging Face; Silero VAD desde GitHub, fijado a la etiqueta `v5.1.2` y verificado por SHA256) y (2) consultar y descargar actualizaciones desde GitHub Releases en las instalaciones con Setup.
- **Sin secretos en el repositorio**: el código no contiene claves, tokens ni credenciales. El workflow de publicación solo usa el `GITHUB_TOKEN` efímero que proporciona GitHub Actions. El CI ejecuta [gitleaks](https://github.com/gitleaks/gitleaks) en cada push y PR para impedir que se suba un secreto por error.
- **Datos locales**: transcripciones y configuración viven en `%LOCALAPPDATA%\VoiceRecorder` sin cifrar; cualquiera con acceso a tu perfil de Windows puede leerlas. Protege tu cuenta y tu disco (por ejemplo, con BitLocker). Reporta cualquier mecanismo que permita exfiltrarlas o ejecutar código con permisos del usuario.
- **Integridad de los paquetes**: cada release publica `SHA256SUMS.txt` y GitHub muestra el digest SHA256 de cada archivo; compruébalo antes de instalar (`Get-FileHash VoiceRecorder-win-Setup.exe`). El instalador aún no está firmado con certificado de código, por lo que Windows SmartScreen puede advertir al ejecutarlo.
