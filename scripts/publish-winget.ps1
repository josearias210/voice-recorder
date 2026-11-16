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

# 2) SHA256 del instalador: primero desde el digest que publica GitHub para
#    cada asset (sin descargar ~100 MB); si no está, se descarga y se calcula.
$hash = $null
$today = Get-Date -Format "yyyy-MM-dd"
try {
    $release = Invoke-RestMethod "https://api.github.com/repos/josearias210/voice-recorder/releases/tags/v$Version"
    $asset = $release.assets | Where-Object name -eq 'VoiceRecorder-win-Setup.exe' | Select-Object -First 1
    if ($asset.digest -match '^sha256:([0-9a-f]{64})$') {
        $hash = $Matches[1]
    }
    if ($release.published_at) {
        $today = ([datetime]$release.published_at).ToString("yyyy-MM-dd")
    }
}
catch {
    Write-Warning "No se pudo consultar la API de GitHub: $($_.Exception.Message)"
}

if (-not $hash) {
    $tmpInstaller = Join-Path $env:TEMP "winget-setup-$Version.exe"
    Write-Host "== Descargando instalador..." -ForegroundColor Cyan
    curl.exe -sL -o $tmpInstaller $url
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $tmpInstaller)) {
        throw "No se pudo descargar $url"
    }

    $hash = (Get-FileHash $tmpInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    Remove-Item $tmpInstaller -Force
}

Write-Host "== SHA256: $hash" -ForegroundColor Cyan

# 3) Parchear manifests
Get-ChildItem $newDir -Filter *.yaml | ForEach-Object {
    $yaml = Get-Content $_.FullName -Raw
    $yaml = $yaml -replace '(?m)^PackageVersion: .*$', "PackageVersion: $Version"
    $yaml = $yaml -replace '(?m)^InstallerUrl: .*$', "InstallerUrl: $url"
    $yaml = $yaml -replace '(?m)^InstallerSha256: .*$', "InstallerSha256: $hash"
    $yaml = $yaml -replace '(?m)^ReleaseDate: .*$', "ReleaseDate: $today"
    $yaml = $yaml -replace '(?m)^(ReleaseNotesUrl: .*/tag/)v[^\s]+$', "`${1}v$Version"
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
