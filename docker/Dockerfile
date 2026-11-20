# syntax=docker/dockerfile:1
#
# Entorno de desarrollo de Voice Recorder sin instalar nada en la máquina.
# SDK .NET 10 (el que usa el CI); incluye el runtime .NET 10, que es el
# TargetFramework del proyecto, para ejecutar las pruebas multiplataforma.
#
# Uso habitual:  .\scripts\docker.ps1 <build|test|publish|lint|secrets|all>

# Etiqueta literal para que Dependabot pueda proponer actualizaciones.
FROM mcr.microsoft.com/dotnet/sdk:10.0

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_XMLDOC_MODE=skip \
    # Permite compilar y publicar los proyectos net10.0-windows (WPF) desde Linux.
    EnableWindowsTargeting=true \
    # bin/obj fuera del repositorio montado: no se mezclan con compilaciones de Windows.
    UseArtifactsOutput=true \
    ArtifactsPath=/artifacts

WORKDIR /src
