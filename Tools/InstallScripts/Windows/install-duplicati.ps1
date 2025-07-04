<#
.SYNOPSIS
    One-touch bootstrap of Duplicati 2 Windows-service deployment.

.DESCRIPTION
    - Installs the Duplicati Windows service
    - Installs the VC++ Redistributable (if not already installed)
    - Creates a TLS certificate for localhost and exports it to C:\ProgramData\Duplicati\localhost.pfx
    - Stores secrets in Windows Credential Manager
    - Generates preload.json using environment variables per
      https://docs.duplicati.com/detailed-descriptions/preload-settings
    - Exports TLS cert to C:\ProgramData\Duplicati\localhost.pfx and trusts it (adds to LocalMachine\Root)
    - Finishes with “Duplicati.WindowsService.exe INSTALL”

.PARAMETER Channel
    The Duplicati update channel to use (default = stable).
    Valid values: stable | beta | experimental | canary

.PARAMETER NonInteractive
    Run in non-interactive mode, using preset values from presets.ini.
    If not set, the script will prompt for user input.

.PARAMETER OverwriteAll
    Overwrite existing configurations, including:
    - Certificate password
    - Database passphrase
    - Web-UI auth passphrase

.PARAMETER KeepArm64
    Keep the ARM64 version of Duplicati if it is installed, even if x64 is available.
    If not set, the script will prefer x64 for VSS compatibility.

.PARAMETER OfflineMode
    Run in offline mode, using local files only.
    If not set, the script will download the latest Duplicati MSI and VC++ Redistributable,
    if no local files are found.

.PARAMETER RequireSystemContext
    Require the script to be run in SYSTEM context.
    If not set, the script will run in the the current user context.
    If the current user is not SYSTEM, the service will not be installed.
    If set, the script will exit with an error if not run in SYSTEM context.

.EXAMPLE
    # Standard install (interactive)
    powershell.exe -ExecutionPolicy Bypass -File install-service.ps1

.EXAMPLE
    # Non-interactive install with preset values
    powershell.exe -ExecutionPolicy Bypass -File install-service.ps1 `
                   -NonInteractive -Channel beta
.EXAMPLE
    # Non-interactive install with preset values and overwrite
    powershell.exe -ExecutionPolicy Bypass -File install-service.ps1 `
                   -NonInteractive -Channel beta -OverwriteAll

#>

[CmdletBinding()]
param(
    [ValidateSet('stable','beta','experimental','canary')]
    [string] $Channel        = 'stable',

    [switch] $NonInteractive,
    [switch] $OverwriteAll,
    [switch] $KeepArm64,
    [switch] $OfflineMode,
    [switch] $RequireSystemContext,

    # ── overrides / presets ───────────────────────────────────────
    [string] $AuthPassphrase,                           # optional CLI override
    [string] $SendHttpJsonUrls,                         # optional CLI override
    [string] $RemoteControlRegisterUrl,                 # optional CLI override
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

if (-not $OverwriteAll) {
    $iniFlag = Get-PresetValue 'OverwriteAll'
    if ($iniFlag -and $iniFlag.Trim().ToLower() -eq 'true') {
        $OverwriteAll = $true
    }
}

if (-not $KeepArm64) {
    $iniFlag = Get-PresetValue 'KeepArm64'
    if ($iniFlag -and $iniFlag.Trim().ToLower() -eq 'true') {
        $KeepArm64 = $true
    }
}

if (-not $OfflineMode) {
    $iniFlag = Get-PresetValue 'OfflineMode'
    if ($iniFlag -and $iniFlag.Trim().ToLower() -eq 'true') {
        $OfflineMode = $true
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
    Write-Error "This installer must be run in SYSTEM context. Exiting."
    exit 1
}

if (-not $runningAsSystem)
{
    $principal = New-Object Security.Principal.WindowsPrincipal(
        [Security.Principal.WindowsIdentity]::GetCurrent())

    $IsAdmin = $principal.IsInRole(
      [Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $IsAdmin) {
        Write-Error "This installer must be run as Administrator or SYSTEM. Exiting."
        exit 1
    }

    Write-Host "Running in user context, service will not be installed."
    Write-Host "All credentials will be stored in the current user's Credential Manager."
    Write-Host "To install the service, run this script in SYSTEM context."
}

# ── Normalise preset path (handles relative + UNC) ─────────────────────
try {
    # If the file OR its parent folders exist, Resolve-Path works and
    # returns a provider-friendly absolute path (UNC OK).
    $PresetPath = (Resolve-Path -LiteralPath $PresetPath -ErrorAction Stop).ProviderPath
} catch {
    # Either the path doesn’t exist yet or Resolve-Path failed on a pure
    # UNC string.  Fall back to .NET’s full-path resolver.
    $PresetPath = [System.IO.Path]::GetFullPath($PresetPath)
}

$ProgressPreference = 'SilentlyContinue'   # tidy downloads

#──────────────────────────  Paths / constants  ────────────────────────
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$VcExeName = 'VC_redist.x64.exe'
$DupSvcAccount = 'SYSTEM'
$DupSvcName = 'Duplicati service'

$ProgramFilesDup = if ([Environment]::Is64BitOperatingSystem) {
    "${env:ProgramW6432}\Duplicati 2"
} else { "${env:ProgramFiles}\Duplicati 2" }

$ProgramDataDup = 'C:\ProgramData\Duplicati'
$AppDataLocalDup = Join-Path $env:LOCALAPPDATA 'Duplicati'

$DuplicatiMsiProperties = 'FORSERVICE=true'
$CredPrefix  = 'Duplicati-'
$CertCredKey = "${CredPrefix}CertPassword"
$DbCredKey   = "${CredPrefix}DatabasePassphrase"
$AuthCredKey = "${CredPrefix}AuthPassphrase"

$LatestJsonUrl = "https://updates.duplicati.com/$Channel/latest-v2.json"

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

function Write-Credential {
    param([string]$Target, [string]$Secret)

    $bytes = [Text.Encoding]::Unicode.GetBytes($Secret)
    $ptr   = [Runtime.InteropServices.Marshal]::AllocHGlobal($bytes.Length)
    [Runtime.InteropServices.Marshal]::Copy($bytes,0,$ptr,$bytes.Length)

    $cred              = [InstallScript.CREDENTIAL]::new()
    $cred.Type         = 1                # CRED_TYPE_GENERIC
    $cred.TargetName   = $Target
    $cred.CredentialBlobSize = $bytes.Length
    $cred.CredentialBlob     = $ptr
    $cred.Persist      = 2                # CRED_PERSIST_LOCAL_MACHINE
    $cred.UserName     = 'Duplicati'

    if (-not ([InstallScript.CredApi]::CredWriteW([ref]$cred,0))) {
        $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw "CredWrite failed ($err)"
    }
    [Runtime.InteropServices.Marshal]::FreeHGlobal($ptr)
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

function Ensure-Credential {
    param(
        [string]$Target,
        [string]$Secret,
        [switch]$Force
    )

    if ($Force -or -not (Read-Credential $Target)) {
        Write-Credential $Target $Secret
        Write-Host "Stored credential $Target (overwrite=$($Force.IsPresent))"
    }
    else {
        Write-Host "Credential $Target already exists - keeping existing value."
    }
}

#──────────────────────────  Helpers  ──────────────────────────────────
function Confirm-Step($Message) {
    if ($NonInteractive) { return }
    $c = Read-Host "$Message (Y/n)"
    if ($c -notmatch '^[Yy]' -and $c -ne '') { Write-Host 'Cancelled.'; exit }
}

function New-RandomString([int]$Bytes = 32) {
    $buf = New-Object byte[] $Bytes
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($buf)
    $rng.Dispose()
    [Convert]::ToBase64String($buf).TrimEnd('=')
}
function SecureStringToPlain([SecureString]$ss) {
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($ss)
    try   { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}
function Test-FileHashMatch ($Path, $ExpectedBase64) {
    $actualHash = Get-FileHash -Path $Path -Algorithm SHA256
    $hex = $actualHash.Hash -replace '[^0-9A-Fa-f]', ''

    # Manually convert hex to bytes (compatible with PS 5.1)
    $bytes = for ($i = 0; $i -lt $hex.Length; $i += 2) {
        [Convert]::ToByte($hex.Substring($i, 2), 16)
    }

    $calc = [Convert]::ToBase64String($bytes)
    return ($calc -ceq $ExpectedBase64)
}
function Get-VersionFromString($str) {
    if ($str -match 'duplicati-(\d+\.\d+\.\d+\.\d+)'){[Version]$Matches[1]}else{[Version]'0.0.0.0'}
}
function Get-InstalledDuplicatiVersion {
    $roots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    )

    $versions = @()

    foreach ($r in $roots) {
        Get-ChildItem $r -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                $p = Get-ItemProperty $_.PSPath
                if ($p.DisplayName -match '^Duplicati\b' -and $p.DisplayVersion) {
                    $versions += [Version]$p.DisplayVersion
                }
            } catch { }
        }
    }

    if ($versions.Count) {
        # Return the highest version found
        return ($versions | Sort-Object -Descending | Select-Object -First 1)
    }

    return [Version]'0.0.0.0'
}

# ────────── Ensure ProgramData\Duplicati with locked-down ACL ─────────
function Ensure-ProgramDataFolder {
    if (-not (Test-Path $ProgramDataDup)) {
        New-Item -Path $ProgramDataDup -ItemType Directory | Out-Null
    }

    # Build a brand-new ACL
    $acl = New-Object System.Security.AccessControl.DirectorySecurity

    # Full control for SYSTEM
    $sidSystem = New-Object System.Security.Principal.SecurityIdentifier 'S-1-5-18'
    $ruleSystem = New-Object System.Security.AccessControl.FileSystemAccessRule `
        ($sidSystem,'FullControl','ContainerInherit, ObjectInherit','None','Allow')
    $acl.AddAccessRule($ruleSystem)

    # Full control for the built-in Administrators group
    $sidAdmins = New-Object System.Security.Principal.SecurityIdentifier 'S-1-5-32-544'
    $ruleAdmins = New-Object System.Security.AccessControl.FileSystemAccessRule `
        ($sidAdmins,'FullControl','ContainerInherit, ObjectInherit','None','Allow')
    $acl.AddAccessRule($ruleAdmins)

    # Disable inheritance and replace existing ACLs with ours
    $acl.SetAccessRuleProtection($true,$false)

    # Apply the ACL
    Set-Acl -Path $ProgramDataDup -AclObject $acl
}

function Ensure-LocalAppDataFolder {
    # Create folder if missing
    if (-not (Test-Path $AppDataLocalDup)) {
        New-Item -Path $AppDataLocalDup -ItemType Directory | Out-Null
    }

    # Build a brand-new ACL (nothing inherited)
    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)   # disable inheritance

    # Full control for SYSTEM
    $sidSystem = New-Object System.Security.Principal.SecurityIdentifier 'S-1-5-18'
    $acl.AddAccessRule( [System.Security.AccessControl.FileSystemAccessRule]::new(
        $sidSystem, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow'))

    # Full control for Administrators
    $sidAdmins = New-Object System.Security.Principal.SecurityIdentifier 'S-1-5-32-544'
    $acl.AddAccessRule( [System.Security.AccessControl.FileSystemAccessRule]::new(
        $sidAdmins, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow'))

    # Full control for the current (interactive) user
    $sidUser = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl.AddAccessRule( [System.Security.AccessControl.FileSystemAccessRule]::new(
        $sidUser, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow'))

    # Apply the ACL
    Set-Acl -Path $AppDataLocalDup -AclObject $acl

    Write-Host "Secured $AppDataLocalDup for user, Administrators, and SYSTEM."
}

# --- call it wherever you need the folder ready ---
Ensure-LocalAppDataFolder

if ($runningAsSystem) {
    Ensure-ProgramDataFolder   # call early (needed for PFX export)
}

#──────────────────────────  VC++ redistributable  ─────────────────────
function Test-VCRedistInstalled {
    $paths=@(
      'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64',
      'HKLM:\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X64')
    foreach($p in $paths){ if((Test-Path $p) -and
         ((Get-ItemProperty $p).Installed -eq 1)){return $true}}
    $false
}

function Local-VCRedistExists {
    $local = Get-ChildItem $ScriptDir -Filter $VcExeName -ea SilentlyContinue | Select-Object -First 1
    return $local -ne $null
}

function Install-VCRedist {
    $local = Get-ChildItem $ScriptDir -Filter $VcExeName -ea SilentlyContinue|Select-Object -First 1
    if($local){
        Write-Host "Using local VC++: $($local.Name)"
        Start-Process $local.FullName -ArgumentList '/install /quiet /norestart' -Wait
        return
    }
    Write-Host 'Downloading VC++ ...'
    $url='https://aka.ms/vs/17/release/vc_redist.x64.exe'
    $tmp=Join-Path $env:TEMP $VcExeName
    Invoke-WebRequest $url -OutFile $tmp
    Start-Process $tmp -ArgumentList '/install /quiet /norestart' -Wait
}

if ($runningAsSystem) {
    if (Test-VCRedistInstalled) {
        Write-Host 'VC++ Redistributable already installed.'
    } elseif ($OfflineMode -and -not (Local-VCRedistExists)) {
        Write-Warning 'VC++ Redistributable not installed, offline mode is enabled and no local installer found.'
        Write-Warning 'This will cause VSS snapshots to fail when running backups.'
    } else {
        Confirm-Step 'Install / update VC++ Redistributable?'
        Install-VCRedist
    }
}

#──────────────────────────  Duplicati installer  ──────────────────────
function Stop-ServiceIfRunning($Name) {
    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue

    if ($service -and $service.Status -eq 'Running') {
        Write-Host "Stopping service $Name ..."
        Stop-Service -Name $Name -Force -ErrorAction Stop
        $service.WaitForStatus('Stopped', '00:00:30') | Out-Null        
    }
}
function Install-Msi($Path,[string]$Args=''){
    Write-Host "Installing $(Split-Path $Path -Leaf)..."
    $p=Start-Process msiexec.exe -ArgumentList "/i `"$Path`" $Args /qn /norestart" -Wait -PassThru
    if($p.ExitCode){throw "MSI exit code $($p.ExitCode)"}
}
function Get-LatestLocalMsi($arch) {
    Get-ChildItem $ScriptDir -Filter "duplicati-*${Channel}*-win-${arch}-gui.msi" -ea SilentlyContinue |
      ForEach-Object{[PSCustomObject]@{File=$_;Ver=Get-VersionFromString $_.Name}} |
      Sort-Object Ver -Descending|Select-Object -First 1
}
function Install-Duplicati {
    $arch=switch($env:PROCESSOR_ARCHITECTURE){'ARM64'{'arm64'}'AMD64'{'x64'}default{'x86'}}
    
    if ($arch -eq 'arm64' -and -not $KeepArm64 -and $runningAsSystem) {
        Write-Host "ARM64 architecture detected, using 'x64' MSI for VSS compatibility."
        $arch = 'x64'
    }

    $instVer=Get-InstalledDuplicatiVersion
    $isInstalled = $instVer -ne '0.0.0.0'
    if ($isInstalled) {
        Write-Host "Found installed Duplicati version: $instVer"
        $actionVerb = 'Update'
    } else {
        Write-Host 'No Duplicati installation found.'
        $actionVerb = 'Install'
    }
    # Local file?
    $pick=Get-LatestLocalMsi($arch)
    if($pick){
        if($instVer -ge $pick.Ver){
            Write-Host "Local MSI v$($pick.Ver) is not newer - skipping."
        }else{
            Confirm-Step "$actionVerb Duplicati ($($pick.File))?"
            Stop-ServiceIfRunning $DupSvcName
            Install-Msi $pick.File.FullName $DuplicatiMsiProperties
            $instVer=$pick.Ver
        }
        return
    }

    if ($OfflineMode) {
        if (-not $isInstalled) {
            Write-Error 'No local Duplicati MSI found, and offline mode is enabled. Failing.'
            exit 1
        }

        Write-Host "Offline mode is enabled with no local MSI, keeping existing Duplicati version $instVer."
        return
    }

    # Remote?
    $json = Invoke-RestMethod -Uri $LatestJsonUrl

    $key="win-$arch-gui.msi"
    if(-not $json.$key){throw "No '$key' in manifest"}
    $remVer=Get-VersionFromString $json.$key.filename
    if($instVer -ge $remVer){
        Write-Host "Installed version is up-to-date ($instVer) - no download needed."
        return
    }

    Confirm-Step "Download and $actionVerb Duplicati ($remVer)?"

    $url=$json.$key.url
    $dest=Join-Path $env:TEMP ($url|Split-Path -Leaf)
    Write-Host "Downloading Duplicati $remVer ..."
    Invoke-WebRequest $url -OutFile $dest
    if(-not (Test-FileHashMatch $dest $json.$key.sha256)){throw 'SHA-256 mismatch on downloaded MSI'}
    Write-Host 'Checksum OK'
    Stop-ServiceIfRunning $DupSvcName
    Install-Msi $dest $DuplicatiMsiProperties
    Remove-Item -Path $dest -Force -ErrorAction SilentlyContinue
}

Install-Duplicati

# ────────── Certificate (create only if PFX absent) ──────────────────
# PS 5.1 support for getting the thumbprint with SecureString password
function Get-PfxThumbprint {
    param (
        [string]          $Path,
        [SecureString]    $SecurePwd
    )
    # Convert SecureString ➜ plain for X509Certificate2
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecurePwd)
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        $cert  = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($Path, $plain)
        return $cert.Thumbprint
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}
if ($OverwriteAll -or -not (Test-Path $PfxPath)) {
    if ((Test-Path $PfxPath)) {
        Write-Host "PFX file already exists at $PfxPath - overwriting."
        Remove-Item -Path $PfxPath -Force
    }

    Write-Host 'Creating localhost TLS certificate ...'
    $cert = New-SelfSignedCertificate -DnsName @('localhost','127.0.0.1') `
        -CertStoreLocation 'Cert:\LocalMachine\My' `
        -FriendlyName 'Duplicati-Localhost' `
        -NotAfter (Get-Date).AddYears(5)

    $certPw = New-RandomString 24
    $secPw  = ConvertTo-SecureString $certPw -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $secPw | Out-Null
    Ensure-Credential $CertCredKey $certPw -Force:$OverwriteAll
    Write-Host "TLS cert exported to $PfxPath"
} else {
    Write-Host "Existing PFX found at $PfxPath - using it."
    $cred = Read-Credential $CertCredKey
    if ($cred) {
        $secPw = $cred | ConvertTo-SecureString -AsPlainText -Force
    } else {
        Write-Warning "PFX exists but credential $CertCredKey not found."
        $secPw = Read-Host 'Enter password for existing PFX' -AsSecureString
    }
}

# Trust the cert if it isn’t already trusted
$thumb = Get-PfxThumbprint -Path $PfxPath -SecurePwd $secPw
$rootStore = Get-ChildItem Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $thumb
if (-not $rootStore) {
    Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation Cert:\LocalMachine\Root `
                          -Password $secPw | Out-Null
    Write-Host 'Certificate added to Trusted Root store.'
} else {
    Write-Host 'Certificate already trusted.'
}

#──────────────────────────  Secrets  ──────────────────────────────────
$dbPass  = New-RandomString 40
Ensure-Credential $DbCredKey $dbPass -Force:$OverwriteAll

# ────────── Resolve Web-UI auth passphrase ────────────────────────────
# Use existing credential if it’s there
$cred = Read-Credential $AuthCredKey
if ($cred -and $cred) {
    $AuthPassphrase = $cred
    Write-Host "Using existing credentials for Web-UI passphrase."
}

if (-not $AuthPassphrase) { $AuthPassphrase = Get-PresetValue 'AuthPassphrase' }
if (-not $AuthPassphrase) {
    if (-not $NonInteractive) {
        $AuthPassphrase = SecureStringToPlain (
            Read-Host 'Enter Web-UI auth passphrase (leave blank for none)' -AsSecureString)
    }
}

if ($AuthPassphrase) {
    Ensure-Credential $AuthCredKey $AuthPassphrase -Force:$OverwriteAll
}

# --------- read preload.json for default values (if file exists) -----
$preloadPath = Join-Path $ProgramFilesDup 'preload.json'
if (-not $SendHttpJsonUrls -or -not $RemoteControlRegisterUrl) {
    if (Test-Path $preloadPath) {
        try {
            $preloadObj = Get-Content $preloadPath -Raw | ConvertFrom-Json
            $dbServer  = $preloadObj.db.server
            $envServer  = $preloadObj.env.server

            if (-not $SendHttpJsonUrls -and $dbServer.'--send-http-json-urls') {
                $SendHttpJsonUrls = $dbServer.'--send-http-json-urls'
                Write-Host "Using --send-http-json-urls from existing preload.json"
            }

            if (-not $RemoteControlRegisterUrl -and $envServer.'DUPLICATI_REMOTE_CONTROL_URL') {
                $RemoteControlRegisterUrl = $envServer.'DUPLICATI_REMOTE_CONTROL_URL'
                Write-Host "Using DUPLICATI_REMOTE_CONTROL_URL from existing preload.json"
            }
        }
        catch {
            Write-Warning "Could not parse $preloadPath : $_"
        }
    }
}

# ────────── Optional “send-http-json-urls” setting ────────────────────
if (-not $SendHttpJsonUrls) { $SendHttpJsonUrls = Get-PresetValue 'SendHttpJsonUrls' }
if (-not $SendHttpJsonUrls -and -not $NonInteractive) {
    Write-Host "`nOptional: Set --send-http-json-urls to enable sending HTTP JSON URLs to the web service."
    Write-Host "You can get this value from: https://app.duplicati.com/app/getting-started/connection-key"
    $input = Read-Host 'Enter value for --send-http-json-urls (leave blank for none)'
    if ($input.Trim()) { $SendHttpJsonUrls = $input.Trim() }
}

# ────────── Optional “register-remote-control” setting ────────────────────
if (-not $RemoteControlRegisterUrl) { $RemoteControlRegisterUrl = Get-PresetValue 'RemoteControlRegisterUrl' }
if (-not $RemoteControlRegisterUrl -and -not $NonInteractive) {
    Write-Host "`nOptional: Enable remote control registration."
    Write-Host "You can generate this value from: https://app.duplicati.com/app/settings/registered-machines"
    $input = Read-Host 'Enter value for the remote control registration (leave blank for none)'
    if ($input.Trim()) { $RemoteControlRegisterUrl = $input.Trim() }
}

# ────────── Build preload.json ────────────────────────────────────────
$argsServer = @(
    '--secret-provider=wincred://',
    '--secret-provider-pattern=!{}',
    "--settings-encryption-key=!{$DbCredKey}",
    "--webservice-sslcertificatefile=$PfxPath",
    "--webservice-sslcertificatepassword=!{$CertCredKey}"
)

$argsTray = @(
    '--hosturl=https://localhost:8200/',
    '--secret-provider=wincred://',
    '--secret-provider-pattern=!{}'
)

$envServer = @{ }

$dbServer = @{ }

if ($RemoteControlRegisterUrl) {
    $argsServer['--register-remote-control'] = $RemoteControlRegisterUrl
    # For backward compatibility with older versions
    $envServer['DUPLICATI_REMOTE_CONTROL_URL'] = $RemoteControlRegisterUrl
}

if ($SendHttpJsonUrls) {
    $dbServer['--send-http-json-urls'] = $SendHttpJsonUrls
}

if ($runningAsSystem) {
    $argsTray += '--no-hosted-server=true'
    $argsServer += "--server-datafolder=$ProgramDataDup"
    $dbServer['--snapshot-policy'] = 'required'
    if ($AuthPassphrase) {
        $argsServer += "--webservice-password=!{$AuthCredKey}"
    }   
} else {
    $argsTray += @(
        "--settings-encryption-key=!{$DbCredKey}",
        "--webservice-sslcertificatefile=$PfxPath",
        "--webservice-sslcertificatepassword=!{$CertCredKey}"
    )

    if ($AuthPassphrase) {
        $argsTray   += "--webservice-password=!{$AuthCredKey}"
   }
}

$preload = @{
    args = @{ 
        server = $argsServer 
        tray = $argsTray
    }
    env = @{ server = $envServer }
    db  = @{ server = $dbServer }
}

$preloadPath = Join-Path $ProgramFilesDup 'preload.json'
$preload | ConvertTo-Json -Depth 4 | Set-Content $preloadPath -Encoding UTF8
Write-Host "preload.json written to $preloadPath"

# ────────── Write newbackup.json (one-time) ───────────────────────────
function Expand-EnvJson {
    param([string]$SourcePath)

    $txt = Get-Content -LiteralPath $SourcePath -Raw

    $pattern = '\$\{([A-Za-z0-9_]+)\}'   # Matches ${VAR}

    $expanded = [regex]::Replace($txt, $pattern, {
        param($m)
        $var = $m.Groups[1].Value
        $val = [System.Environment]::GetEnvironmentVariable($var)
        if ([string]::IsNullOrEmpty($val)) { $m.Value } else { $val }
    })

    $out = Join-Path $env:TEMP ("expanded_{0}.json" -f ([guid]::NewGuid()))
    Set-Content -LiteralPath $out -Value $expanded -Encoding UTF8
    return $out
}

$newBackupPath      = Join-Path $ProgramFilesDup 'newbackup.json'
$localNewBackupPath = Join-Path $ScriptDir       'newbackup.json'
if (Test-Path $localNewBackupPath) {
    $tempNewBackupPath = Expand-EnvJson -SourcePath $localNewBackupPath
    Copy-Item -Path $tempNewBackupPath -Destination $newBackupPath -Force
    Write-Host "Copied local newbackup.json to $newBackupPath (overwrote if present)."
    Remove-Item -Path $tempNewBackupPath -Force -ErrorAction SilentlyContinue
}

function Add-DuplicatiTrayShortcut {
    param(
        [string]$ExePath = "$env:ProgramFiles\Duplicati 2\Duplicati.GUI.TrayIcon.exe",
        [string]$Args    = '',
        [string]$LinkName = 'Duplicati Tray.lnk'
    )

    # Shared Startup folder for every user
    $startupAll = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Startup'
    $linkPath   = Join-Path $startupAll $LinkName

    if (Test-Path $linkPath) {
        Write-Host "Startup shortcut already exists at $linkPath"
        return
    }

    $shell = New-Object -ComObject WScript.Shell
    $lnk   = $shell.CreateShortcut($linkPath)
    $lnk.TargetPath = $ExePath
    $lnk.Arguments  = $Args
    $lnk.WorkingDirectory = Split-Path $ExePath
    $lnk.IconLocation     = "$ExePath,0"
    $lnk.Save()

    Write-Host "Created startup shortcut for all users:"
    Write-Host "  $linkPath -> $ExePath $Args"
}

function Start-ProcessUnelevated {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string]$Arguments  = '',
        [string]$WorkingDirectory = (Split-Path $FilePath)
    )

    if (-not (Test-Path $FilePath)) {
        throw "Executable not found: $FilePath"
    }

    $shell = New-Object -ComObject 'Shell.Application'
    # 6th arg is window style (1 = normal, 0 = hidden, etc.)
    $null  = $shell.ShellExecute($FilePath, $Arguments, $WorkingDirectory, 'open', 1)

    Write-Host "Launched '$FilePath' unelevated."
}

if ($runningAsSystem) {
    #──────────────────────────  Service INSTALL  ─────────────────────────
    $svcExe=Join-Path $ProgramFilesDup 'Duplicati.WindowsService.exe'
    Write-Host 'Installing Duplicati Windows service ...'
    if (-not (Get-Service Duplicati -ErrorAction SilentlyContinue)) {
        & $svcExe INSTALL
    }

    # ---------- start or restart the service (PS-5.1 compatible) ----------
    try {
        $service = Get-Service -Name $DupSvcName -ErrorAction Stop

        if ($service.Status -eq 'Running') {
            Write-Host "Service '$DupSvcName' is already running - restarting"
            Restart-Service -Name $DupSvcName -Force -ErrorAction Stop

            # get a fresh object and wait until it reports Running
            $service = Get-Service -Name $DupSvcName
            $service.WaitForStatus('Running', '00:00:15')

            Write-Host "Service '$DupSvcName' restarted."
        }
        else {
            Write-Host "Starting service '$DupSvcName'"
            Start-Service -Name $DupSvcName -ErrorAction Stop
            Start-Sleep -Seconds 5
            $service = Get-Service -Name $DupSvcName
            $service.WaitForStatus('Running', '00:00:15')
            Write-Host "Service '$DupSvcName' is now running."
        }
    }
    catch {
        Write-Warning "Could not start or restart service '$DupSvcName' : $_"
    }

} else {
    Add-DuplicatiTrayShortcut
    Write-Host "Starting Duplicati Tray Icon in non-privileged user context..."
    Start-ProcessUnelevated -FilePath "$ProgramFilesDup\Duplicati.GUI.TrayIcon.exe"
}

#──────────────────────────  Configure initial backup if backup-template.json exist  ─────────────────────────
$localBackupTemplate = Join-Path $ScriptDir 'backup-template.json'
function Get-BackupNamesFromOutput {
    param(
        [Parameter(ValueFromPipeline)]
        [AllowEmptyString()]
        [string]$RawLine
    )

    process {
        if (-not [string]::IsNullOrWhiteSpace($RawLine)) {
            $parts = $RawLine -split ':', 2
            if ($parts.Count -eq 2) {
                ($parts[1]).Trim()
            }
        }
    }
}

if (Test-Path $localBackupTemplate) {
    $serverUtilExe = Join-Path $ProgramFilesDup 'Duplicati.CommandLine.ServerUtil.exe'
    $dbPass = Read-Credential $DbCredKey
    $cred = Read-Credential $AuthCredKey

    if (-not (Test-Path $serverUtilExe)) {
        Write-Error "Duplicati.ServerUtil.exe not found at $serverUtilExe. Cannot configure initial backup."    
    } elseif (-not $cred) {
        Write-Error "Due to a bug in ServerUtil, an authentication passphrase is required to configure the initial backup."
    } elseif ($instVer -lte [Version]'2.1.0.118') {
        Write-Error "Due to a bug in Duplicati.CommandLine.ServerUtil in this version, it is not possible to configure the initial backup."
    } else {
        Write-Host "Configuring initial backup from $localBackupTemplate ..."
        $expandedTemplate = Expand-EnvJson -SourcePath $localBackupTemplate

        # Run the server util to configure the backup
        $dataFolder = if ($runningAsSystem) {
            $ProgramDataDup
        } else {
            $AppDataLocalDup
        }

        $baseArgs = @(
            "--secret-provider=wincred://",
            '--secret-provider-pattern=!{}',
            "--settings-encryption-key=!{$DbCredKey}",
            '--hosturl=https://localhost:8200',
            "--server-datafolder=$dataFolder"
        )

        if ($cred) { $baseArgs += "--password=!{$AuthCredKey}" }
        $args = @('list-backups') + $baseArgs
        Write-Host "Listing backups to check if the backup already exists ..."
        $raw = & $serverUtilExe @args 2>$null 
        $names = $raw | Get-BackupNamesFromOutput

        $json  = Get-Content -LiteralPath $expandedTemplate -Raw | ConvertFrom-Json
        $name  = $json.Backup.Name

        if (-not $name) {
            throw "JSON has no Backup.Name value."
        }

        if ($names -contains $name) {
            Write-Host "Backup '$name' already exists - skipping configuration."
        } else {
            Write-Host "Adding backup '$name' ..."
            $args = @(
                'import',
                $expandedTemplate,
                '--import-metadata'
            ) + $baseArgs 
            & $serverUtilExe @args
            Write-Host "Arguments used: $($args -join ' ')"
            Write-Host "Backup '$name' configured successfully."
        }

        Remove-Item -Path $expandedTemplate -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "No backup-template.json found in script directory, skipping initial backup configuration."
}

if ($runningAsSystem) {
    Write-Host "All done - open https://localhost:8200 to access the UI"    
} else {
    Write-Host "Duplicati Tray Icon started in user context, UI should open automatically."
    Write-Host "If it does not, open https://localhost:8200 in your browser."
}