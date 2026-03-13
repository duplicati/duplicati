<#
.SYNOPSIS
    Launch another PowerShell script in the SYSTEM context.
    • Always adds -NonInteractive to the inner script (if not already present).
    • Waits and captures the inner script’s output by default.
      -NoWait      → fire-and-forget
      -NoCapture   → suppress log capture / echo

.EXAMPLE
    # Default: wait & show output
    .\run-as-system.ps1 -ScriptPath '.\install-duplicati.ps1'

.EXAMPLE
    # Run with arguments, wait & show output
    .\run-as-system.ps1 -ScriptPath '.\uninstall-duplicati.ps1' -ScriptArguments '-RemoveData -RemoveCert -RemoveCreds'

.EXAMPLE
    # Fire-and-forget, no output
    .\run-as-system.ps1 -ScriptPath '.\install-duplicati.ps1' -NoWait -NoCapture
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript( { Test-Path $_ -PathType Leaf } )]
    [string]$ScriptPath,

    [string]$ScriptArguments = '',

    [switch]$NoWait,
    [switch]$NoCapture
)

# ─── Helpers ────────────────────────────────────────────────────────────────
function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal  $id).
        IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function New-UniqueTaskName { 'RunAsSystem_' + ([guid]::NewGuid().ToString('N')) }
function New-TempOutputFile { Join-Path $env:TEMP ('sysrun_{0}.log' -f ([guid]::NewGuid().ToString('N'))) }

# ─── Safety check ───────────────────────────────────────────────────────────
if (-not (Test-IsAdmin)) {
    throw 'Run-AsSystem.ps1 must be started from an *elevated* PowerShell session.'
}

# PowerShell 7 compatibility
if ($PSVersionTable.PSEdition -eq 'Core') {
    Import-Module ScheduledTasks -ErrorAction Stop
}

# Make sure we have a full path
$ScriptPath = (Resolve-Path -LiteralPath $ScriptPath).ProviderPath

# Ensure -NonInteractive is present
if ($ScriptArguments -notmatch '(?i)(^|\s)-NonInteractive(\s|$)') {
    $ScriptArguments = "$ScriptArguments -NonInteractive".Trim()
}

$Wait          = -not $NoWait
$CaptureOutput = -not $NoCapture
$tmpOut        = if ($CaptureOutput) { New-TempOutputFile } else { $null }

# Escape quotes in path/args
$escapedPath = '"' + $ScriptPath.Replace('"','""') + '"'
$escapedArgs = if ($ScriptArguments) { $ScriptArguments } else { "" }

$innerCmd = "& { & $escapedPath $escapedArgs *>&1"   # merge all streams
if ($CaptureOutput) { $innerCmd += " | Tee-Object -FilePath `"$tmpOut`"" }
$innerCmd += " }"

# Wrap it in outer powershell.exe call
$outerArgs = "-NoLogo -NoProfile -ExecutionPolicy Bypass -Command `"$innerCmd`""

# ─── Create task ────────────────────────────────────────────────────────────
$taskName  = New-UniqueTaskName
$action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $outerArgs
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -Compatibility Win8 -Hidden `
              -StartWhenAvailable `
              -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$settings.ExecutionTimeLimit = 'PT0S'  # no timeout

try {
    Register-ScheduledTask -TaskName $taskName `
                           -Action    $action `
                           -Principal $principal `
                           -Settings  $settings | Out-Null

    Write-Host "Scheduled SYSTEM task '$taskName' created; starting..."
    Start-ScheduledTask -TaskName $taskName
    
    function Is-SchedStatus($code) {
        $schedStatuses = 0x41300..0x4130A
        return $schedStatuses -contains $code
    }
        
    if ($Wait -or $CaptureOutput) {
        Write-Host "Waiting for SYSTEM task '$taskName' to finish..."
        do {
            Start-Sleep 1
            $info  = Get-ScheduledTaskInfo -TaskName $taskName
            $state = $info.State
            $code  = $info.LastTaskResult
        } until (
            ($state -ne 'Running' -and -not (Is-SchedStatus $code)) `
            -or ($code -eq 0)                                      # success
        )
    }

    if ($CaptureOutput -and $tmpOut -and (Test-Path $tmpOut)) {
        Write-Host "`n===== OUTPUT FROM SYSTEM TASK ====="
        Get-Content $tmpOut
        Write-Host "===== END OUTPUT =====`n"
    }

    $exit = (Get-ScheduledTaskInfo -TaskName $taskName).LastTaskResult
    if ($exit -ne 0) {
        Write-Warning "Inner script exited with code $exit"
    }

}
finally {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }
    if ($tmpOut) { Remove-Item $tmpOut -ErrorAction SilentlyContinue }
}