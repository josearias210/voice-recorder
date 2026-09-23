<#
.SYNOPSIS
    Compila y publica Voice Recorder (portable ZIP).
.EXAMPLE
    .\scripts\build.ps1                          # Release win-x64
    .\scripts\build.ps1 -SkipZip                 # solo publish
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "",
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
