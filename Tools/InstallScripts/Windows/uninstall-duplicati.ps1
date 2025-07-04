<#
.SYNOPSIS
    Cleanly uninstall Duplicati 2 Windows-service deployment.

.DESCRIPTION
    - Stops the Windows service (if present)  
    - Uninstalls the Duplicati MSI  
    - OPTIONAL switches  
        -RemoveData      > Deletes C:\ProgramData\Duplicati and C:\Program Files\Duplicati 2 
        -RemoveCert      > Deletes localhost PFX and removes the cert from LocalMachine\My & Root  
        -RemoveCreds     > Deletes Credential-Manager keys  
    - Reboots are NOT forced (the MSI rarely needs one).

.PARAMETER RemoveData
    Remove the ProgramData\Duplicati folder.

.PARAMETER RemoveCert
    Delete the exported PFX and the certificate(s) from the machine store.

.PARAMETER RemoveCreds
    Delete SYSTEM-scope Credential-Manager entries (Duplicati-*).

.PARAMETER RequireSystemContext
    If set, the script will exit with an error if not run in SYSTEM context.

.EXAMPLE
    # Standard uninstall (leave data, cert and creds in place)
    powershell.exe -ExecutionPolicy Bypass -File uninstall-duplicati.ps1

.EXAMPLE
    # Full wipe
    powershell.exe -ExecutionPolicy Bypass -File uninstall-duplicati.ps1 `
                   -RemoveData -RemoveCert -RemoveCreds
#>

[CmdletBinding()]
param(
    [switch]$RemoveData,
    [switch]$RemoveCert,
    [switch]$RemoveCreds,
    [switch]$RequireSystemContext,

    [string] $PresetPath = "$PSScriptRoot\presets.ini"  # default INI file
)

# ──────────────────────────  Load presets  ─────────────────────────────
function Get-PresetValue ([string]$Key) {
    if (-not (Test-Path $PresetPath)) { return $null }
    foreach ($line in Get-Content $PresetPath) {
        if ($line -match '^\s*[#;]') { continue }
        if ($line -match '^\s*([^=]+?)\s*=\s*(.+?)\s*$') {
            if ($Matches[1].Trim() -ieq $Key) { return $Matches[2].Trim() }
        }
    }
    return $null
}

if (-not $RemoveData) {
    $iniFlag = Get-PresetValue 'RemoveData'
    if ($iniFlag -and $iniFlag.Trim().ToLower() -eq 'true') {
        $RemoveData = $true
    }
}

if (-not $RemoveCert) {
    $iniFlag = Get-PresetValue 'RemoveCert'
    if ($iniFlag -and $iniFlag.Trim().ToLower() -eq 'true') {
        $RemoveCert = $true
    }
}

if (-not $RemoveCreds) {
    $iniFlag = Get-PresetValue 'RemoveCreds'
    if ($iniFlag -and $iniFlag.Trim().ToLower() -eq 'true') {
        $RemoveCreds = $true
    }
}

if (-not $RequireSystemContext) {
    $iniFlag = Get-PresetValue 'RequireSystemContext'
    if ($iniFlag -and $iniFlag.Trim().ToLower() -eq 'true') {
        $RequireSystemContext = $true
    }
}

#──────────────────────────  Service guard  ──────────────────────────────
$runningAsSystem = (
    [System.Security.Principal.WindowsIdentity]::GetCurrent().User -eq
    (New-Object System.Security.Principal.SecurityIdentifier('S-1-5-18'))
)

if (-not $runningAsSystem -and $RequireSystemContext) {
    Write-Error "This uninstaller must be run in SYSTEM context. Exiting."
    exit 1
} 

if (-not $runningAsSystem) {
    $principal = New-Object Security.Principal.WindowsPrincipal(
        [Security.Principal.WindowsIdentity]::GetCurrent())

    $IsAdmin = $principal.IsInRole(
      [Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $IsAdmin) {
        Write-Error "This installer must be run as Administrator or SYSTEM. Exiting."
        exit 1
    }

    Write-Host "Running in user context, service will not be uninstalled."
    Write-Host "All credentials will be removed from the current user's Credential Manager."
    Write-Host "To uninstall the service, run this script in SYSTEM context."
}

$ErrorActionPreference = 'Stop'
$DupSvcName        = 'Duplicati'
$ProgramFilesDup   = "${env:ProgramFiles}\Duplicati 2"
$ProgramDataDup    = 'C:\ProgramData\Duplicati'
$CertFriendlyName  = 'Duplicati-Localhost'
$CredPrefix        = 'Duplicati-'            # all keys start with this
$AppDataLocalDup = Join-Path $env:LOCALAPPDATA 'Duplicati'

if ($runningAsSystem) {
    $PfxPath        = Join-Path $ProgramDataDup 'localhost.pfx'
} else {
    $PfxPath        = Join-Path $AppDataLocalDup 'localhost.pfx'
}


# ── helper: Handle Credentials without a Nuget Package ─────────────
if (-not ('InstallScript.CREDENTIAL' -as [type])) {
Add-Type @"
using System;
using System.Runtime.InteropServices;

namespace InstallScript {

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREDENTIAL
    {
        public UInt32 Flags;
        public UInt32 Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public UInt32 CredentialBlobSize;
        public IntPtr CredentialBlob;
        public UInt32 Persist;
        public UInt32 AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    public static class CredApi
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredWriteW(ref CREDENTIAL cred, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredReadW(string target, uint type, uint flags, out IntPtr pcred);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern void CredFree(IntPtr buffer);
    }
}
"@
}

function Read-Credential {
    param([string]$Target)

    $p = [IntPtr]::Zero
    if (-not ([InstallScript.CredApi]::CredReadW($Target, 1, 0, [ref]$p))) { return $null }

    try {
        $c = [Runtime.InteropServices.Marshal]::PtrToStructure($p, [type]([InstallScript.CREDENTIAL]))
        if ($c.CredentialBlobSize -eq 0) { return $null }

        [Runtime.InteropServices.Marshal]::PtrToStringUni(
            $c.CredentialBlob,
            $c.CredentialBlobSize / 2   # WCHAR count
        )
    }
    finally {
        [InstallScript.CredApi]::CredFree($p)
    }
}

# helper to delete a matching cert from a given store
function Remove-CertByMatch {
    param(
        [string]$StoreName,
        [string]$Thumb
    )

    if (-not $thumb) {
        Write-Host "No thumbprint found; will use fallback cert name match for cleanup."
    }

    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
                 $StoreName,'LocalMachine')
    $store.Open('ReadWrite')

    $matches = if ($Thumb) {
        $store.Certificates | Where-Object { $_.Thumbprint -ieq $Thumb }
    } else {
        # fallback: friendly-name AND CN=localhost AND key container contains "Duplicati"
        $store.Certificates | Where-Object {
            $_.FriendlyName -eq 'Duplicati-Localhost' -and
            $_.Subject -match 'CN=localhost' -and
            $_.HasPrivateKey -and
            $_.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName -like '*Duplicati*'
        }
    }

    if ($matches) {
        $store.RemoveRange($matches)
        Write-Host "Removed $($matches.Count) certificate(s) from LM\$StoreName"
    } else {
        Write-Host "No matching certificate(s) found in LM\$StoreName"
    }
    $store.Close()
}

# ───────────────── Stop & delete service ──────────────────────────────
if ($runningAsSystem) {
    try {
        $svc = Get-Service -Name $DupSvcName -ErrorAction Stop
        if ($svc.Status -eq 'Running') {
            Write-Host "Stopping service $DupSvcName ..."
            Stop-Service -Name $DupSvcName -Force -ErrorAction Stop
            $svc.WaitForStatus('Stopped','00:00:20')
        }
        Write-Host "Removing service $DupSvcName ..."
        sc.exe delete $DupSvcName | Out-Null
    } catch {
        Write-Host "Service '$DupSvcName' not found."
    }
} else {
    $proc = Get-Process -Name 'Duplicati.GUI.TrayIcon' -ErrorAction SilentlyContinue
    if ($proc) {
        Write-Host "Found $($proc.Count) instance(s) of Duplicati.GUI.TrayIcon.exe - stopping..."
        $proc | Stop-Process -Force
    } else {
        Write-Host "Duplicati.GUI.TrayIcon.exe is not running."
    }
}

# ───────────────── Uninstall MSI package ──────────────────────────────
function Get-DuplicatiProductCode {
    $keys = @(
      'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
      'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')
    foreach ($root in $keys) {
        Get-ChildItem $root -ErrorAction SilentlyContinue | ForEach-Object {
            $p = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
            if ($p.DisplayName -match '^Duplicati\b') { return $_.PSChildName }
        }
    }
    return $null
}

$productCode = Get-DuplicatiProductCode
if ($productCode) {
    Write-Host "Uninstalling Duplicati (ProductCode $productCode) ..."
    $msi = Start-Process msiexec.exe `
              -ArgumentList "/x $productCode /qn /norestart" `
              -Wait -PassThru

    switch ($msi.ExitCode) {
        0      { Write-Host 'Duplicati uninstalled.' }
        3010   { 
            Write-Host 'Duplicati uninstalled (reboot required).'
            $global:RebootNeeded = $true   # mark for later
        }
        default { throw "MSI uninstall failed with exit code $($msi.ExitCode)" }
    }
} else {
    Write-Host 'Duplicati MSI not found - nothing to remove.'
}

if (-not $runningAsSystem) {
    $startupAll = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup'
    $linkPath   = Join-Path $startupAll 'Duplicati Tray.lnk'

    if (Test-Path $linkPath) {
        Write-Host "Removing startup link $linkPath ..."
        Remove-Item $linkPath -Force -ErrorAction SilentlyContinue
    } else {
        Write-Host "Startup link not found: $linkPath"
    }
}

# ───────────────── Optional: remove certificate/PFX ───────────────────
if ($RemoveCert) {
    $thumb = $null
    if (Test-Path $PfxPath) {
        # read password from credential store
        $pwPlain = Read-Credential 'Duplicati-CertPassword'
        if (-not $pwPlain) {
            Write-Warning "Certificate credential missing; cannot verify thumbprint."
        } else {
            # open PFX to get exact thumbprint
            try {
                $certObj = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2
                $certObj.Import($PfxPath, $pwPlain, 'EphemeralKeySet')
                $thumb = $certObj.Thumbprint
            } catch {
                Write-Warning "Cannot load PFX - $_"
            }
        }

    }

    # remove the cert from My and Root stores
    Remove-CertByMatch 'My'   $thumb
    Remove-CertByMatch 'Root' $thumb

    if (Test-Path $PfxPath -ea SilentlyContinue) {
        Remove-Item $PfxPath -Force -ErrorAction SilentlyContinue
        Write-Host "Deleted PFX file $PfxPath"
    }
}

# ───────────────── Optional: remove ProgramData ───────────────────────
if ($RemoveData) {
    if ($runningAsSystem) {    
        if (Test-Path $ProgramDataDup -ea SilentlyContinue) {
            Write-Host "Deleting $ProgramDataDup ..."
            Remove-Item $ProgramDataDup -Recurse -Force -ErrorAction SilentlyContinue
        } else {
            Write-Host "ProgramData folder not found."
        }
    } else {
        if (Test-Path $AppDataLocalDup -ea SilentlyContinue) {
            Write-Host "Deleting $AppDataLocalDup ..."
            Remove-Item $AppDataLocalDup -Recurse -Force -ErrorAction SilentlyContinue
        } else {
            Write-Host "AppData\Local\Duplicati folder not found."
        }
    }
}

# ───────────────── Optional: remove Program Files\Duplicati ───────────────────────
if ($RemoveData) {
    if (Test-Path $ProgramFilesDup -ea SilentlyContinue) {
        Write-Host "Deleting $ProgramFilesDup ..."
        Remove-Item $ProgramFilesDup -Recurse -Force -ErrorAction SilentlyContinue
    } else {
        Write-Host "Program Files\Duplicati 2 folder not found."
    }
}

# ───────────────── Optional: remove Credential-Manager keys ───────────
if ($RemoveCreds) {
    $targets = & cmdkey /list | Select-String '^\s*Target:' |
    ForEach-Object {
        # Grab everything after "Target:" and trim
        $t = ($_ -split ':',2)[1].Trim()
        # Keep only keys that contain our prefix
        if ($t -like "*$CredPrefix*") { $t }
    }

    foreach ($t in $targets) {
        Write-Host "Deleting credential $t"
        cmdkey /delete:$t | Out-Null
    }

    if (-not $targets) {
        Write-Host "No Duplicati credentials found."
    }
}

Write-Host "Duplicati uninstallation complete."

if ($RebootNeeded) {
    Write-Warning "A system reboot is recommended to finalize the uninstall."
}