<#
.SYNOPSIS
    Publica Voice Recorder y (opcionalmente) empaqueta con Velopack para probar
    el flujo de actualización en local.
.EXAMPLE
    .\scripts\build.ps1                                  # publish + zip portable
    .\scripts\build.ps1 -VpkVersion 0.9.2                # + paquetes vpk en artifacts\vpk-test
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "",
    [string]$VpkVersion = "",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

$versionArgs = @()
if ($Version) {
    $versionArgs += "-p:Version=$Version"
}

Write-Host "== Publicando VoiceRecorder ($Configuration / $Runtime)..." -ForegroundColor Cyan
dotnet publish "$root/src/VoiceRecorder.App/VoiceRecorder.App.csproj" `
    -c $Configuration -r $Runtime --self-contained true `
    @versionArgs `
    -o "$root/publish"

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish falló"
}

# Paquetes Velopack (Setup + releases para auto-update). Útil para probar el
# flujo de actualización en local con VR_UPDATE_SOURCE.
if ($VpkVersion) {
    Write-Host "== Empaquetando Velopack v$VpkVersion..." -ForegroundColor Cyan
    vpk pack `
        --packId VoiceRecorder `
        --packVersion $VpkVersion `
        --packDir "$root/publish" `
        --mainExe VoiceRecorder.exe `
        --packAuthors "josearias210" `
        --icon "$root/src/VoiceRecorder.App/Assets/app.ico" `
        --outputDir "$root/artifacts/vpk-test"

    if ($LASTEXITCODE -ne 0) {
        throw "vpk pack falló"
    }

    Write-Host "Paquetes vpk listos en artifacts\vpk-test" -ForegroundColor Green
    Write-Host "Prueba de update: instala el Setup 0.9.1, crea 0.9.2 y lanza la app" -ForegroundColor Yellow
    Write-Host "con `"\\$env:VR_UPDATE_SOURCE = 'artifacts\vpk-test'`"" -ForegroundColor Yellow
}

if (-not $SkipZip) {
    $version = if ($Version) { $Version } else { "dev" }
    New-Item -ItemType Directory -Force -Path "$root/artifacts" | Out-Null
    $zip = "$root/artifacts/VoiceRecorder-portable-$version.zip"
    Write-Host "== Empaquetando $zip..." -ForegroundColor Cyan
    if (Test-Path $zip) {
        Remove-Item $zip -Force
    }
    Compress-Archive -Path "$root/publish/*" -DestinationPath $zip
    Write-Host "Portable listo: $zip" -ForegroundColor Green
}
