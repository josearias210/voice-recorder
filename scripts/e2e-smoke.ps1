<#
.SYNOPSIS
    Prueba E2E de humo: arranca Voice Recorder, recorre sus pantallas y guarda
    una captura de cada una.

.DESCRIPTION
    Pensado para el CI de Windows (job «E2E» de ci.yml), donde hay escritorio
    pero ningún dispositivo de audio. Pasos:
      1. Con -AcceptLegalNotice, prepara un perfil de prueba en
         %LOCALAPPDATA%\VoiceRecorder: settings.json con el aviso legal
         aceptado y el análisis experimental activo, una sesión de
         demostración con transcripción, resumen y análisis (datos ficticios,
         scripts/e2e/demo-session.json) y un registro de costes de ejemplo.
         Sin ese perfil la app se quedaría esperando el diálogo del aviso.
      2. Lanza el ejecutable y espera a su ventana principal.
      3. Comprueba con UI Automation los controles principales («Iniciar»,
         «Historial», «Configuración») y que «Historial» abre el panel lateral.
      4. Recorre las demás pantallas: Configuración (arriba y abajo),
         Transcripción…, Resumen…, Notion…, Costes…, Backup externo… y el chat
         de la sesión de demostración (con el resumen y el análisis).
      5. Guarda una captura PNG de cada pantalla en <OutDir>/screens y un
         manifiesto (screens.json) con su título, para publicarlas en el PR.
    Si una pantalla falla, se anota, se captura lo que haya y se sigue con las
    demás; al final sale con código distinto de 0.

    No lo ejecutes en tu equipo con -AcceptLegalNotice: sobrescribe tu
    %LOCALAPPDATA%\VoiceRecorder (settings.json, sesiones y costes).

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
$outPath = Resolve-Path $OutDir
$screensDir = Join-Path $outPath 'screens'
New-Item -ItemType Directory -Force -Path $screensDir | Out-Null
$report = Join-Path $outPath 'report.md'
$manifest = Join-Path $outPath 'screens.json'

Add-Type -AssemblyName System.Drawing, System.Windows.Forms, System.Security, UIAutomationClient, UIAutomationTypes
Add-Type -Namespace E2E -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
'@

$UIA = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]

# ---------------------------------------------------------------- helpers

function Save-Capture([string]$Path, $Element = $null) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    if ($Element) {
        $rect = $Element.Current.BoundingRectangle
        if (-not $rect.IsEmpty -and $rect.Width -gt 0 -and $rect.Height -gt 0) {
            # Window bounds clipped to the screen.
            $left = [Math]::Max([int]$rect.Left, $bounds.Left)
            $top = [Math]::Max([int]$rect.Top, $bounds.Top)
            $right = [Math]::Min([int]$rect.Right, $bounds.Right)
            $bottom = [Math]::Min([int]$rect.Bottom, $bounds.Bottom)
            if ($right -gt $left -and $bottom -gt $top) {
                $bounds = New-Object System.Drawing.Rectangle $left, $top, ($right - $left), ($bottom - $top)
            }
        }
    }

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

function Find-Element($Parent, [string]$Name, $ControlType = $null) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($UIA::NameProperty, $Name)
    if ($ControlType) {
        $condition = New-Object System.Windows.Automation.AndCondition(
            $condition,
            (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, $ControlType)))
    }

    $Parent.FindFirst($Tree::Descendants, $condition)
}

function Invoke-Button($Window, [string]$Name) {
    $button = Find-Element $Window $Name ([System.Windows.Automation.ControlType]::Button)
    if (-not $button) {
        throw "No se encontró el botón «$Name»."
    }

    # WPF runs the click asynchronously, so a modal dialog does not block this call.
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# Waits for a top-level window of the app whose title matches (wildcards allowed).
function Wait-Window([string]$Title, [int]$Seconds = 20) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    $condition = New-Object System.Windows.Automation.PropertyCondition($UIA::ProcessIdProperty, $script:process.Id)
    while ((Get-Date) -lt $deadline) {
        foreach ($window in $UIA::RootElement.FindAll($Tree::Children, $condition)) {
            if ($window.Current.Name -like $Title) {
                return $window
            }
        }

        Start-Sleep -Milliseconds 300
    }

    throw "No apareció la ventana «$Title» en $Seconds s."
}

function Show-Front($Window) {
    $handle = [IntPtr]$Window.Current.NativeWindowHandle
    if ($handle -ne [IntPtr]::Zero) {
        [E2E.Native]::SetForegroundWindow($handle) | Out-Null
    }

    Start-Sleep -Milliseconds 800
}

function Close-Window($Window) {
    try {
        $Window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    }
    catch {
        Write-Warning "No se pudo cerrar «$($Window.Current.Name)»: $($_.Exception.Message)"
    }

    Start-Sleep -Milliseconds 500
}

$screens = [System.Collections.Generic.List[object]]::new()
$checks = [System.Collections.Generic.List[string]]::new()
$failures = [System.Collections.Generic.List[string]]::new()

# Captures a window (or the whole screen) as screens/NN-<slug>.png and records it in the manifest.
function Add-Screen([string]$Slug, [string]$Title, $Element = $null) {
    $file = '{0:D2}-{1}.png' -f ($screens.Count + 1), $Slug
    Save-Capture (Join-Path $screensDir $file) $Element
    $screens.Add([ordered]@{ title = $Title; file = $file })
    $checks.Add("Captura: $Title")
}

# Runs one screen step; a failure is recorded and captured, and the next steps still run.
function Invoke-Step([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
    }
    catch {
        $failures.Add("$Name — $($_.Exception.Message)")
        try {
            Add-Screen ('fallo-' + ($Name -replace '[^\w]+', '-').ToLowerInvariant()) "❌ $Name (fallo)"
        }
        catch {
            Write-Warning "No se pudo capturar el fallo: $($_.Exception.Message)"
        }
    }
}

# ---------------------------------------------------------------- test profile

function Initialize-Profile {
    $legal = Get-Content (Join-Path $root 'src/VoiceRecorder.App/Services/LegalNotice.cs') -Raw
    if ($legal -notmatch 'public const string Version = "([^"]+)"') {
        throw 'No se encontró LegalNotice.Version en LegalNotice.cs.'
    }

    $version = $Matches[1]
    $dataDir = Join-Path $env:LOCALAPPDATA 'VoiceRecorder'
    if (Test-Path $dataDir) {
        Remove-Item -Recurse -Force $dataDir
    }

    New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
    [ordered]@{
        ModelFileName              = $null
        LanguageCode               = 'es'
        MicrophoneId               = $null
        AcceptedLegalNoticeVersion = $version
        HistoryOpen                = $false
        ReviewEnabled              = $true
        NotionDatabaseId           = '01234567-89ab-cdef-0123-456789abcdef'
    } | ConvertTo-Json | Set-Content -Path (Join-Path $dataDir 'settings.json') -Encoding utf8

    $sessionDir = Join-Path $dataDir 'Sesiones/2026/11/20261124-100000-00001'
    New-Item -ItemType Directory -Force -Path $sessionDir | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'e2e/demo-session.json') (Join-Path $sessionDir 'session.json')
    Copy-Item (Join-Path $PSScriptRoot 'e2e/demo-costs.jsonl') (Join-Path $dataDir 'transcripciones.jsonl')
    $checks.Add("Perfil de prueba: aviso legal $version aceptado, análisis experimental activo y una sesión de demostración")
}

# ---------------------------------------------------------------- run

$process = $null
$fatal = $null
try {
    if ($AcceptLegalNotice) {
        Initialize-Profile
    }

    $exe = Resolve-Path $AppPath
    $process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
    $checks.Add("Proceso iniciado (PID $($process.Id))")

    # Waits for the main window (its title includes «Transcriptor de llamadas»).
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $main = $null
    while ((Get-Date) -lt $deadline -and -not $main) {
        if ($process.HasExited) {
            throw "La aplicación se cerró al arrancar (código $($process.ExitCode))."
        }

        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero -and $process.MainWindowTitle -like '*Transcriptor de llamadas*') {
            $main = $UIA::FromHandle($process.MainWindowHandle)
        }
        else {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $main) {
        throw "La ventana principal no apareció en $TimeoutSeconds s (título actual: '$($process.MainWindowTitle)')."
    }

    $checks.Add("Ventana principal: «$($process.MainWindowTitle)»")
    [E2E.Native]::ShowWindow($process.MainWindowHandle, 3) | Out-Null   # SW_MAXIMIZE
    [E2E.Native]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Start-Sleep -Seconds 3

    foreach ($name in 'Iniciar', 'Historial', 'Configuración') {
        if (-not (Find-Element $main $name)) {
            throw "No se encontró el elemento «$name» en la ventana principal."
        }

        $checks.Add("Elemento visible: «$name»")
    }

    # The history panel is hidden by default: open it and check it shows up.
    if (Find-Element $main 'Historial de sesiones') {
        throw 'El historial debería estar oculto al arrancar.'
    }

    Add-Screen 'principal' 'Pantalla principal' $main
    Invoke-Button $main 'Historial'
    Start-Sleep -Seconds 1
    if (-not (Find-Element $main 'Historial de sesiones')) {
        throw 'El botón «Historial» no abrió el panel del historial.'
    }

    $checks.Add('El botón «Historial» abre el panel lateral')

    # Selects the demo session so the history shows its actions.
    $list = $main.FindFirst($Tree::Descendants, (New-Object System.Windows.Automation.PropertyCondition($UIA::AutomationIdProperty, 'HistoryList')))
    $item = if ($list) {
        $list.FindFirst($Tree::Children, (New-Object System.Windows.Automation.PropertyCondition($UIA::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
    }

    if ($item) {
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 800
        $checks.Add('La sesión de demostración aparece en el historial')
    }
    elseif ($AcceptLegalNotice) {
        $failures.Add('Historial — no aparece la sesión de demostración')
    }

    Add-Screen 'historial' 'Pantalla principal con el historial' $main
}
catch {
    $fatal = $_.Exception.Message
    try {
        Add-Screen 'fallo-arranque' '❌ Arranque (fallo)'
    }
    catch {
        Write-Warning "No se pudo guardar la captura del fallo: $($_.Exception.Message)"
    }
}

if (-not $fatal) {
    Invoke-Step 'Configuración' {
        Invoke-Button $main 'Configuración'
        $settings = Wait-Window 'Configuración'
        Show-Front $settings
        Add-Screen 'configuracion' 'Configuración' $settings

        $scroll = $settings.FindFirst($Tree::Descendants, (New-Object System.Windows.Automation.PropertyCondition($UIA::IsScrollPatternAvailableProperty, $true)))
        if ($scroll) {
            $scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 100)
            Start-Sleep -Milliseconds 600
            Add-Screen 'configuracion-abajo' 'Configuración (final: resumen y destino, experimental, apariencia)' $settings
            $scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, 0)
        }

        # Each dialog opens from Settings, is captured and closed.
        $dialogs = @(
            @{ Button = 'Transcripción…'; Title = 'Transcripción · motores'; Slug = 'transcripcion'; Name = 'Configuración › Transcripción…' },
            @{ Button = 'Resumen…'; Title = 'Resumen · OpenAI'; Slug = 'resumen'; Name = 'Configuración › Resumen…' },
            @{ Button = 'Notion…'; Title = 'Destino · Notion'; Slug = 'notion'; Name = 'Configuración › Notion…' },
            @{ Button = 'Costes…'; Title = 'Costes de transcripción'; Slug = 'costes'; Name = 'Configuración › Costes…' },
            @{ Button = 'Backup externo…'; Title = 'Backup externo · Azure Blob Storage'; Slug = 'backup'; Name = 'Configuración › Backup externo…' }
        )
        foreach ($dialog in $dialogs) {
            Invoke-Step $dialog.Name {
                Invoke-Button $settings $dialog.Button
                $window = Wait-Window $dialog.Title
                Show-Front $window
                Add-Screen $dialog.Slug $dialog.Name $window
                Close-Window $window
            }
        }

        Close-Window $settings
    }

    if ($AcceptLegalNotice) {
        Invoke-Step 'Chat de la sesión' {
            Show-Front $main
            Invoke-Button $main 'Abrir chat'
            $chat = Wait-Window '* · Voice Recorder'
            Show-Front $chat

            # Expands the summary and the (collapsed) communication review.
            $expanders = $chat.FindAll($Tree::Descendants, (New-Object System.Windows.Automation.PropertyCondition($UIA::IsExpandCollapsePatternAvailableProperty, $true)))
            foreach ($expander in $expanders) {
                try {
                    $expander.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
                }
                catch {
                    # Not an expander that can be opened (e.g. a combo box): ignore.
                }
            }

            Start-Sleep -Milliseconds 600
            foreach ($name in 'Regenerar resumen', 'Reanalizar conversación', 'Subir a Notion') {
                if (-not (Find-Element $chat $name ([System.Windows.Automation.ControlType]::Button))) {
                    throw "No se encontró el botón «$name» en el chat."
                }
            }

            $checks.Add('El chat muestra el resumen, el análisis y sus botones')
            Add-Screen 'chat' 'Chat de la sesión (resumen y análisis de comunicación)' $chat
            Close-Window $chat
        }
    }

    if ($process.HasExited) {
        $failures.Add("La aplicación se cerró durante la prueba (código $($process.ExitCode)).")
    }
    else {
        $checks.Add('La aplicación sigue en ejecución tras recorrer las pantallas')
    }
}

if ($process -and -not $process.HasExited) {
    # Closing the window only hides it in the tray: end the process.
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
}

ConvertTo-Json -InputObject @($screens) -Depth 3 | Set-Content -Path $manifest -Encoding utf8

$status = if ($fatal) {
    "❌ Falló: $fatal"
}
elseif ($failures.Count -gt 0) {
    "❌ La app arranca, pero fallaron $($failures.Count) pantalla(s)"
}
else {
    "✅ La aplicación arranca y se recorrieron $($screens.Count) pantallas"
}

$lines = @('### E2E de humo · Voice Recorder', '', $status, '') +
    ($checks | ForEach-Object { "- $_" }) +
    ($failures | ForEach-Object { "- ❌ $_" })
$lines | Set-Content -Path $report -Encoding utf8
$lines | ForEach-Object { Write-Host $_ }

if ($fatal -or $failures.Count -gt 0) {
    exit 1
}
