<#
.SYNOPSIS
    Genera los manifests de winget para una nueva versión de Voice Recorder.

.DESCRIPTION
    Los manifests base viven en winget\Josearias210.VoiceRecorder\<versión>\.
    Este script crea la carpeta de la nueva versión a partir de la más reciente:
    descarga el instalador del release, calcula su SHA256 y parchea
    versión/URL/hash/fecha. Después valida con `winget validate`.

    Publicar en winget = PR del contenido de esa carpeta al repositorio
    microsoft/winget-pkgs (ruta: manifests/j/Josearias210/VoiceRecorder/<versión>).
    La PRIMERA alta requiere revisión de Microsoft; las siguientes suelen
    mergearse rápido.

.EXAMPLE
    .\scripts\publish-winget.ps1 -Version 1.2.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [string]$PackageIdentifier = "Josearias210.VoiceRecorder"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$baseDir = "$root/winget/$PackageIdentifier"
$url = "https://github.com/josearias210/voice-recorder/releases/download/v$Version/VoiceRecorder-win-Setup.exe"

# 1) Localizar la versión anterior más reciente como plantilla
$templateDir = Get-ChildItem $baseDir -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne $Version } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1

if (-not $templateDir) {
    throw "No hay manifests previos en $baseDir. Crea la primera versión a mano."
}

Write-Host "== Generando manifests v$Version desde v$($templateDir.Name)..." -ForegroundColor Cyan
$newDir = "$baseDir/$Version"
New-Item -ItemType Directory -Force -Path $newDir | Out-Null
Copy-Item "$($templateDir.FullName)/*.yaml" $newDir -Force

# 2) Descargar el instalador del release y calcular su hash
$tmpInstaller = Join-Path $env:TEMP "winget-setup-$Version.exe"
Write-Host "== Descargando instalador..." -ForegroundColor Cyan
curl.exe -sL -o $tmpInstaller $url
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $tmpInstaller)) {
    throw "No se pudo descargar $url"
}

$hash = (Get-FileHash $tmpInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
$today = Get-Date -Format "yyyy-MM-dd"
Remove-Item $tmpInstaller -Force

# 3) Parchear manifests
Get-ChildItem $newDir -Filter *.yaml | ForEach-Object {
    $yaml = Get-Content $_.FullName -Raw
    $yaml = $yaml -replace '(?m)^PackageVersion: .*$', "PackageVersion: $Version"
    $yaml = $yaml -replace '(?m)^InstallerUrl: .*$', "InstallerUrl: $url"
    $yaml = $yaml -replace '(?m)^InstallerSha256: .*$', "InstallerSha256: $hash"
    $yaml = $yaml -replace '(?m)^ReleaseDate: .*$', "ReleaseDate: $today"
    Set-Content $_.FullName $yaml -Encoding UTF8
}

# 4) Validar
if (Get-Command winget -ErrorAction SilentlyContinue) {
    Write-Host "== Validando manifests..." -ForegroundColor Cyan
    winget validate $newDir
    if ($LASTEXITCODE -ne 0) {
        throw "winget validate falló"
    }
}

Write-Host ""
Write-Host "Manifests v$Version listos en: $newDir" -ForegroundColor Green
Write-Host ""
Write-Host "Para publicar en winget:" -ForegroundColor Yellow
Write-Host "  1. Fork de https://github.com/microsoft/winget-pkgs"
Write-Host "  2. Copia la carpeta a: manifests/j/Josearias210/VoiceRecorder/$Version/"
Write-Host "     (gh repo fork microsoft/winget-pkgs --clone + copia + commit + push)"
Write-Host "  3. Abre PR con título: '$PackageIdentifier version $Version'"
Write-Host ""
Write-Host "  Alternativa automática (una vez aprobada la primera alta):"
Write-Host "     wingetcreate update $PackageIdentifier --version $Version --urls `"$url`" --submit --token <PAT>"
