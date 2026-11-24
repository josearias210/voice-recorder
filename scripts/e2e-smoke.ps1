<#
.SYNOPSIS
    Prueba E2E de humo: arranca Voice Recorder, comprueba que la ventana
    principal carga y guarda una captura de pantalla.

.DESCRIPTION
    Pensado para el CI de Windows (job «E2E» de ci.yml), donde hay escritorio
    pero ningún dispositivo de audio. Pasos:
      1. Con -AcceptLegalNotice, da por aceptado el aviso legal en un
         settings.json nuevo; si no, la app se quedaría esperando ese diálogo.
      2. Lanza el ejecutable y espera a su ventana principal.
      3. Maximiza la ventana y comprueba con UI Automation que se ven los
         controles principales («Iniciar», «Historial de sesiones»).
      4. Guarda una captura de toda la pantalla y comprueba que la app sigue viva.
    Sale con código distinto de 0 si algo falla; siempre que pueda deja la
    captura, también la del fallo.

    No lo ejecutes en tu equipo con -AcceptLegalNotice: sobrescribe tu
    %LOCALAPPDATA%\VoiceRecorder\settings.json.

.EXAMPLE
    .\scripts\e2e-smoke.ps1 -AppPath publish\VoiceRecorder.exe -OutDir artifacts\e2e -AcceptLegalNotice
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AppPath,

    [string]$OutDir = 'artifacts/e2e',

    [int]$TimeoutSeconds = 90,

    [switch]$AcceptLegalNotice
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$screenshot = Join-Path (Resolve-Path $OutDir) 'app.png'
$report = Join-Path (Resolve-Path $OutDir) 'report.md'

Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes
Add-Type -Namespace E2E -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
'@

function Save-Screenshot([string]$Path) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Find-Element($Window, [string]$Name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

$checks = [System.Collections.Generic.List[string]]::new()
$process = $null
$failure = $null
try {
    if ($AcceptLegalNotice) {
        $legal = Get-Content (Join-Path $root 'src/VoiceRecorder.App/Services/LegalNotice.cs') -Raw
        if ($legal -notmatch 'public const string Version = "([^"]+)"') {
            throw 'No se encontró LegalNotice.Version en LegalNotice.cs.'
        }

        $dataDir = Join-Path $env:LOCALAPPDATA 'VoiceRecorder'
        New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
        @{ ModelFileName = $null; LanguageCode = $null; MicrophoneId = $null; AcceptedLegalNoticeVersion = $Matches[1] } |
            ConvertTo-Json | Set-Content -Path (Join-Path $dataDir 'settings.json') -Encoding utf8
        $checks.Add("Aviso legal $($Matches[1]) aceptado para la prueba")
    }

    $exe = Resolve-Path $AppPath
    $process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
    $checks.Add("Proceso iniciado (PID $($process.Id))")

    # Espera a la ventana principal (su título incluye «Transcriptor de llamadas»).
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $window = $null
    while ((Get-Date) -lt $deadline -and -not $window) {
        if ($process.HasExited) {
            throw "La aplicación se cerró al arrancar (código $($process.ExitCode))."
        }

        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero -and $process.MainWindowTitle -like '*Transcriptor de llamadas*') {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        }
        else {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $window) {
        throw "La ventana principal no apareció en $TimeoutSeconds s (título actual: '$($process.MainWindowTitle)')."
    }

    $checks.Add("Ventana principal: «$($process.MainWindowTitle)»")
    [E2E.Native]::ShowWindow($process.MainWindowHandle, 3) | Out-Null   # SW_MAXIMIZE
    [E2E.Native]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Start-Sleep -Seconds 3

    foreach ($name in 'Iniciar', 'Historial de sesiones', 'Transcripción…') {
        if (-not (Find-Element $window $name)) {
            throw "No se encontró el elemento «$name» en la ventana principal."
        }

        $checks.Add("Elemento visible: «$name»")
    }

    Save-Screenshot $screenshot
    $checks.Add('Captura guardada')

    Start-Sleep -Seconds 2
    if ($process.HasExited) {
        throw "La aplicación se cerró después de cargar (código $($process.ExitCode))."
    }

    $checks.Add('La aplicación sigue en ejecución tras cargar')
}
catch {
    $failure = $_.Exception.Message
    try {
        Save-Screenshot $screenshot
    }
    catch {
        Write-Warning "No se pudo guardar la captura del fallo: $($_.Exception.Message)"
    }
}
finally {
    if ($process -and -not $process.HasExited) {
        # Cerrar la ventana solo la oculta en la bandeja: se termina el proceso.
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}

$status = if ($failure) { "❌ Falló: $failure" } else { '✅ La aplicación arranca y la ventana principal carga' }
$lines = @("### E2E de humo · Voice Recorder", '', $status, '') + ($checks | ForEach-Object { "- $_" })
$lines | Set-Content -Path $report -Encoding utf8
$lines | ForEach-Object { Write-Host $_ }

if ($failure) {
    exit 1
}
