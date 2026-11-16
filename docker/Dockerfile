# syntax=docker/dockerfile:1
#
# Entorno de desarrollo de Voice Recorder sin instalar nada en la máquina.
# SDK .NET 10 (el que usa el CI) + runtime .NET 8 (el TargetFramework del
# proyecto) para poder ejecutar las pruebas multiplataforma.
#
# Uso habitual:  .\scripts\docker.ps1 <build|test|publish|lint|secrets|all>

# Etiquetas literales para que Dependabot pueda proponer actualizaciones.
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime

FROM mcr.microsoft.com/dotnet/sdk:11.0

# Añade el runtime .NET 8 junto al del SDK (se usa para ejecutar los tests net8.0).
COPY --from=runtime /usr/share/dotnet/shared/Microsoft.NETCore.App /usr/share/dotnet/shared/Microsoft.NETCore.App

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_XMLDOC_MODE=skip \
    # Permite compilar y publicar los proyectos net8.0-windows (WPF) desde Linux.
    EnableWindowsTargeting=true \
    # bin/obj fuera del repositorio montado: no se mezclan con compilaciones de Windows.
    UseArtifactsOutput=true \
    ArtifactsPath=/artifacts

WORKDIR /src
