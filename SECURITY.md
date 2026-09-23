# Política de Seguridad

## Versiones soportadas

| Versión | Soporte |
|---------|---------|
| 1.0.x   | ✅      |
| < 1.0   | ❌      |

## Reportar una vulnerabilidad

Si descubres una vulnerabilidad de seguridad, **por favor no abras un issue público**.

1. Usa [GitHub Security Advisories](../../security/advisories/new) ("Report a vulnerability") para un reporte privado, o
2. Contacta al mantenedor directamente.

Incluye: descripción, pasos para reproducir, impacto estimado y, si es posible, una prueba de concepto. Recibirás respuesta en un plazo razonable (objetivo: 72 h) y se publicará un parche y un advisory coordinado.

## Alcance y consideraciones de diseño

Voice Recorder procesa audio de llamadas en local. Puntos sensibles del diseño:

- **Sin telemetría**: la app no hace peticiones de red salvo descargas de modelos iniciadas por el usuario (desde servidores oficiales de Whisper.net/GitHub).
- **Datos locales**: transcripciones y configuración viven en `%LOCALAPPDATA%\VoiceRecorder` sin cifrar; un atacante con acceso al disco del usuario puede leerlas. Reporta cualquier mecanismo que permita exfiltrarlas o ejecutar código con permisos del usuario.
- **Instalador**: los releases se publican adjuntos a GitHub Releases; valida el hash o firma cuando esté disponible.
