# Duplicati Shell Extension Registration Script
# Run this script as Administrator to register the shell extension

param(
    [switch]$Unregister
)

$ErrorActionPreference = "Stop"

# GUIDs for the overlay handlers
$overlays = @{
    "DuplicatiBackedUp" = "E4B5F8A3-9C1D-4F2E-B6A7-8D3C5E6F7A9B"
    "DuplicatiWarning" = "E4B5F8A3-9C1D-4F2E-B6A7-8D3C5E6F7A9C"
    "DuplicatiError" = "E4B5F8A3-9C1D-4F2E-B6A7-8D3C5E6F7A9D"
    "DuplicatiSyncing" = "E4B5F8A3-9C1D-4F2E-B6A7-8D3C5E6F7A9E"
}

$overlayKeyPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers"

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script writes to HKLM and must be run from an elevated (Run as Administrator) PowerShell."
    exit 1
}

# Find the comhost DLL: next to this script in an installed copy, or the
# newest build output when run from the source folder
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$dllName = "Duplicati.ShellExtension.comhost.dll"
$dllPath = Join-Path $scriptDir $dllName
if (-not (Test-Path $dllPath)) {
    # Explorer can only load an extension built for the machine's own architecture
    $rid = switch ($env:PROCESSOR_ARCHITECTURE) {
        "ARM64" { "win-arm64" }
        "AMD64" { "win-x64" }
        default { "win-x86" }
    }
    $dllPath = Get-ChildItem -Path (Join-Path $scriptDir "bin") -Recurse -Filter $dllName -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -like "*\$rid" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $dllPath) {
        Write-Error "No $rid build found under $scriptDir\bin. Build with: dotnet build -c Debug -r $rid"
        exit 1
    }
}

if ($Unregister) {
    Write-Host "Unregistering Duplicati Shell Extension..."

    if ($dllPath) {
        Write-Host "Unregistering COM server: $dllPath"
        Start-Process -FilePath regsvr32.exe -ArgumentList "/s", "/u", "`"$dllPath`"" -Wait -NoNewWindow
    }

    # Remove the COM classes even if the DLL is gone or was registered from elsewhere
    foreach ($guid in $overlays.Values) {
        $clsid = "Registry::HKEY_CLASSES_ROOT\CLSID\{$guid}"
        if (Test-Path $clsid) {
            Remove-Item -Path $clsid -Recurse -Force
            Write-Host "Removed COM class {$guid}"
        }
    }

    foreach ($name in $overlays.Keys) {
        # Add leading spaces to ensure high priority (Windows sorts alphabetically)
        $keyName = "   $name"
        $fullPath = Join-Path $overlayKeyPath $keyName

        if (Test-Path $fullPath) {
            Remove-Item -Path $fullPath -Force
            Write-Host "Removed: $keyName"
        }
    }

    Write-Host "Unregistration complete. Please restart Explorer or reboot."
} else {
    Write-Host "Registering Duplicati Shell Extension..."

    if (-not $dllPath) {
        Write-Error "$dllName not found next to the script or under $scriptDir\bin"
        Write-Host "Please build the project first."
        exit 1
    }
    Write-Host "Using: $dllPath"

    if ($dllPath -like "\\*" -or (Get-Item $dllPath).PSDrive.DisplayRoot) {
        Write-Warning "The DLL is on a network location. Explorer may refuse to load shell extensions from there; copy the build output to a local folder if no overlays appear."
    }

    # Register COM server and verify that it actually succeeded
    Write-Host "Registering COM server..."
    $regsvr = Start-Process -FilePath regsvr32.exe -ArgumentList "/s", "`"$dllPath`"" -Wait -PassThru -NoNewWindow
    if ($regsvr.ExitCode -ne 0) {
        Write-Error "regsvr32 failed with exit code $($regsvr.ExitCode). Run 'regsvr32 `"$dllPath`"' without /s to see the error. Exit code 5 usually means access denied when writing the registry, exit code 3 that the DLL could not be loaded (missing x64 .NET runtime)."
        exit 1
    }

    foreach ($guid in $overlays.Values) {
        if (-not (Test-Path "Registry::HKEY_CLASSES_ROOT\CLSID\{$guid}\InprocServer32")) {
            Write-Error "COM class {$guid} was not registered by the comhost"
            exit 1
        }
    }
    Write-Host "COM classes registered"

    foreach ($name in $overlays.Keys) {
        $guid = $overlays[$name]
        # Add leading spaces to ensure high priority (Windows sorts alphabetically)
        $keyName = "   $name"
        $fullPath = Join-Path $overlayKeyPath $keyName

        # Create the registry key
        if (-not (Test-Path $fullPath)) {
            New-Item -Path $fullPath -Force | Out-Null
        }

        # Set the default value to the CLSID
        Set-ItemProperty -Path $fullPath -Name "(Default)" -Value "{$guid}"
        Write-Host "Registered: $keyName -> {$guid}"
    }

    Write-Host "Registration complete. Please restart Explorer or reboot."
    Write-Host ""
    Write-Host "To restart Explorer without rebooting:"
    Write-Host "  Stop-Process -Name explorer -Force"
    Write-Host "  Start-Process explorer"
}
