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

if ($Unregister) {
    Write-Host "Unregistering Duplicati Shell Extension..."

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

    # Get the path to the DLL
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $dllPath = Join-Path $scriptDir "Duplicati.ShellExtension.comhost.dll"

    if (-not (Test-Path $dllPath)) {
        Write-Error "DLL not found at: $dllPath"
        Write-Host "Please build the project first."
        exit 1
    }

    # Register COM server
    Write-Host "Registering COM server..."
    regsvr32 /s $dllPath

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
