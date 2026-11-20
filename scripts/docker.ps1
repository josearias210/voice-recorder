<#
.SYNOPSIS
    Compila, prueba y empaqueta Voice Recorder con Docker, sin instalar .NET
    ni ninguna otra herramienta en la máquina.

.DESCRIPTION
    Requiere solo Docker Desktop (modo contenedores Linux). Cada tarea se
    ejecuta con docker compose (ver docker-compose.yml) montando el repositorio.

    Tareas:
      build    Compila toda la solución (incluida la app WPF) con -warnaserror.
      test     Ejecuta las pruebas multiplataforma (núcleo, transcripción, SQLite).
      publish  Genera la app portable para Windows en artifacts\docker-publish.
      run      publish + abre VoiceRecorder.exe (no necesita .NET instalado).
      format   Comprueba el formato del código según .editorconfig.
      lint     Revisa los workflows de GitHub Actions (actionlint).
      secrets  Busca claves o secretos en todo el historial (gitleaks).
      hooks    Activa el hook pre-commit que revisa cada commit con gitleaks.
      all      build + test + format + lint + secrets (lo mismo que el CI de Linux).
      shell    Abre una terminal dentro del contenedor de .NET.
      clean    Borra artifacts\ y los volúmenes de caché de Docker del proyecto.

    Las pruebas que dependen de WPF/WASAPI (Windows) se ejecutan en el CI de
    GitHub (windows-latest); en Docker se ejecuta el subconjunto portable.

.EXAMPLE
    .\scripts\docker.ps1 test
.EXAMPLE
    .\scripts\docker.ps1 run
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'test', 'publish', 'run', 'format', 'lint', 'secrets', 'hooks', 'all', 'shell', 'clean')]
    [string]$Task = 'all'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    docker compose @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') falló (código $LASTEXITCODE)."
    }
}

function Invoke-Task {
    param([string]$Name)
    Write-Host "== $Name" -ForegroundColor Cyan
    Invoke-Compose run --rm $Name
}

try {
    docker info --format '{{.OSType}}' 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker no está disponible. Abre Docker Desktop (modo contenedores Linux) y vuelve a intentarlo.'
    }

    switch ($Task) {
        'all' {
            Invoke-Compose build --quiet build
            foreach ($name in 'build', 'test', 'format', 'lint', 'secrets') {
                Invoke-Task $name
            }
            Write-Host 'Todo correcto.' -ForegroundColor Green
        }
        'run' {
            Invoke-Compose build --quiet publish
            Invoke-Task 'publish'
            $exe = Join-Path $root 'artifacts\docker-publish\VoiceRecorder.exe'
            Write-Host "== Abriendo $exe" -ForegroundColor Cyan
            Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
        }
        'shell' {
            Invoke-Compose run --rm build bash
        }
        'hooks' {
            git config core.hooksPath .githooks
            if ($LASTEXITCODE -ne 0) { throw 'No se pudo activar el hook pre-commit.' }
            docker pull --quiet zricethezav/gitleaks:v8.30.1 | Out-Null
            Write-Host 'Hook pre-commit activado: cada commit se revisará con gitleaks.' -ForegroundColor Green
        }
        'clean' {
            Invoke-Compose down --volumes --remove-orphans
            if (Test-Path "$root\artifacts") {
                Remove-Item "$root\artifacts" -Recurse -Force
            }
            Write-Host 'Limpio.' -ForegroundColor Green
        }
        default {
            if ($Task -in 'build', 'test', 'publish', 'format') {
                Invoke-Compose build --quiet $Task
            }
            Invoke-Task $Task
            if ($Task -eq 'publish') {
                Write-Host "App portable lista en: $root\artifacts\docker-publish\VoiceRecorder.exe" -ForegroundColor Green
            }
        }
    }
}
finally {
    Pop-Location
}
