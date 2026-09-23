<#
.SYNOPSIS
    Genera/actualiza los manifests de winget para Voice Recorder y (opcionalmente)
    los envía automáticamente al repositorio microsoft/winget-pkgs.

.DESCRIPTION
    La alta inicial en winget requiere un PR al repositorio comunitario
    microsoft/winget-pkgs (revisado por Microsoft). Este script usa wingetcreate.

    1) Genera los manifests apuntando al instalador del release indicado.
    2) Con -Token (PAT de GitHub), crea el fork y envía el PR automáticamente.
       Sin -Token, deja los manifests en artifacts\winget\ para PR manual.

.EXAMPLE
    .\scripts\publish-winget.ps1 -Version 1.1.0
    .\scripts\publish-winget.ps1 -Version 1.1.0 -Token ghp_xxx   # PR automático
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [string]$PackageIdentifier = "Josearias210.VoiceRecorder",

    [string]$Token = ""
)

$ErrorActionPreference = "Stop"

$url = "https://github.com/josearias210/voice-recorder/releases/download/v$Version/VoiceRecorder-win-Setup.exe"

# wingetcreate: herramienta oficial de Microsoft para crear/actualizar manifests
if (-not (Get-Command wingetcreate -ErrorAction SilentlyContinue)) {
    Write-Host "== Instalando wingetcreate..." -ForegroundColor Cyan
    dotnet tool install --global wingetcreate
}

New-Item -ItemType Directory -Force -Path "$PSScriptRoot/../artifacts/winget" | Out-Null
Push-Location "$PSScriptRoot/../artifacts/winget"

Write-Host "== Generando manifests para $PackageIdentifier v$Version..." -ForegroundColor Cyan
wingetcreate new $url `
    --version $Version `
    --package-identifier $PackageIdentifier `
    --package-name "Voice Recorder" `
    --publisher "josearias210" `
    --author "josearias210" `
    --package-url "https://github.com/josearias210/voice-recorder" `
    --license-url "https://github.com/josearias210/voice-recorder/blob/main/LICENSE" `
    --short-description "Transcriptor local de llamadas en tiempo real" `
    --description "Transcribe llamadas de cualquier aplicación (Meet, Teams, Zoom...) en tiempo real, 100% local con Whisper. Sin guardar audio."

if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "wingetcreate new falló"
}

if ($Token) {
    Write-Host "== Enviando PR a microsoft/winget-pkgs..." -ForegroundColor Cyan
    wingetcreate submit $PackageIdentifier --version $Version --token $Token --replace
    if ($LASTEXITCODE -ne 0) {
        Pop-Location
        throw "wingetcreate submit falló"
    }
    Write-Host "PR enviado a winget-pkgs. Queda pendiente de revisión de Microsoft." -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "Manifests generados en artifacts\winget\" -ForegroundColor Green
    Write-Host "Para enviar el PR manualmente:" -ForegroundColor Yellow
    Write-Host "  1. Haz fork de https://github.com/microsoft/winget-pkgs"
    Write-Host "  2. Copia los manifests a manifests/j/$($PackageIdentifier -replace '\.', '/')/$Version/"
    Write-Host "  3. Abre un PR con el título: '$PackageIdentifier version $Version'"
    Write-Host "  (o re-ejecuta este script con -Token <PAT> para enviarlo automáticamente)"
}

Pop-Location
