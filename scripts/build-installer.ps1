<#
.SYNOPSIS
    Compila el instalador con Inno Setup (requiere Inno Setup 6 instalado).
.EXAMPLE
    .\scripts\build-installer.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# 1) Publish
& "$PSScriptRoot/build.ps1" -Configuration $Configuration -Runtime $Runtime -Version $Version -SkipZip

# 2) Localizar ISCC (Inno Setup 6)
$candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "No se encontró ISCC.exe. Instala Inno Setup 6: winget install JRSoftware.InnoSetup"
}

# 3) Compilar instalador
Write-Host "== Compilando instalador v$Version..." -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" "$root/installer/VoiceRecorder.iss"

if ($LASTEXITCODE -ne 0) {
    throw "ISCC falló"
}

Write-Host "Instalador listo: $root/installer/output/VoiceRecorder-Setup-$Version.exe" -ForegroundColor Green
