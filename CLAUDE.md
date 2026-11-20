# Voice Recorder — instrucciones para Claude

## Todo PR prepara la versión nueva

**Siempre que se cree un pull request, debe incluir la preparación de la versión siguiente.** Así cada cambio que llega a `main` queda listo para publicarse y no se acumulan cambios sin versión.

En el mismo PR:

1. **Sube la versión** en `Directory.Build.props` (`<Version>`), según SemVer:
   - parche (1.3.4 → 1.3.5): correcciones, dependencias, documentación, CI;
   - menor (1.3.x → 1.4.0): funciones nuevas o cambios visibles para el usuario;
   - mayor: cambios incompatibles.
   Si ya hay un PR abierto que sube la versión, no la subas otra vez: añade tus cambios a sus notas.
2. **Crea `docs/releases/<versión>.md`**: el release workflow falla si falta. Describe los cambios para el usuario (qué cambia, no cómo) y termina con las secciones «Instalación» y el requisito de Windows, como las notas anteriores.
3. **Si cambia el aviso legal o la privacidad** (`DISCLAIMER.md`, `PRIVACY.md`): actualiza la fecha y la versión en su cabecera y sube `LegalNotice.Version` en `src/VoiceRecorder.App/Services/LegalNotice.cs`, para que la app vuelva a pedir la aceptación. Cualquier salida nueva de datos fuera del equipo debe reflejarse en PRIVACY, DISCLAIMER, SECURITY y README.
4. Revisa `SECURITY.md` (tabla de versiones soportadas) y el `README.md` si lo cambiado los afecta.

**No crees la etiqueta `vX.Y.Z` ni publiques el release sin que el usuario lo pida**: la etiqueta lanza el workflow que publica el instalador y actualiza Scoop. El PR solo deja la versión preparada.

## Herramientas: solo en Docker

No uses .NET, Node ni otras herramientas instaladas en la máquina. Todo se ejecuta en contenedores con:

```powershell
.\scripts\docker.ps1 all      # build + test + format + lint + secrets
```

Antes de abrir un PR deben pasar `build` (con `-warnaserror`), `test`, `format`, `lint` y `secrets`. Las pruebas de WPF/WASAPI solo corren en el CI de Windows.

## Convenciones

- UI y documentación en español; identificadores y comentarios de código en inglés. Consulta `CONTRIBUTING.md`.
- Commits con [Conventional Commits](https://www.conventionalcommits.org/es/).
- **Nunca subas secretos** (claves, tokens, contraseñas). Activa el hook con `.\scripts\docker.ps1 hooks`.
- La app es una herramienta para que un participante transcriba su propia llamada: no añadas modos ocultos, grabación encubierta ni envío automático de datos (ver `DISCLAIMER.md`).
- Al migrar paquetes, comprueba que no queden vulnerabilidades: `dotnet list package --vulnerable --include-transitive` (en Docker).
