# Duplicati 2 Windows Service Installer

This script [`install-service.ps1`](./install-service.ps1) provides a one-touch, idempotent bootstrap process to install and configure Duplicati 2 as a Windows service. It handles installation, certificate generation, credential storage, and service initialization—all with support for automation.

## Features

- Supports Duplicati channels: `stable` (default), `beta`, `experimental`, `canary`
- Detects and skips VC++ redist and Duplicati installation if already up to date
- Generates a trusted self-signed TLS certificate for `localhost`
- Stores passwords and secrets in the Windows Credential Manager (SYSTEM context)
- Generates `preload.json` and `newbackup.json` with predefined variables
- Supports optional presets via `presets.ini`
- Fully automated with `-NonInteractive` flag
- Can run in SYSTEM context (required for writing SYSTEM-owned credentials)
- Optional overwrite of config, cert, and credential values via `-OverwriteAll`

## Requirements

- PowerShell 5.1 or later
- Run as SYSTEM (required for correct certificate and credential installation)
- Access to internet (unless local MSI and VC redist is provided, use -OfflineMode to enforce it)

## Usage

The script will not run unless it is in the SYSTEM context, because otherwise it cannot get access to the Windows Credential store for the SYSTEM user that the service will later query.

The script will look in the folder where the script is located for the VC Redist and Duplicati MSI packages.

It also looks for two optional files:

- `presets.ini` (configuration values)
- `newbackup.json` (defaults for new backups)

From a command prompt, use the [psexec](https://learn.microsoft.com/en-us/sysinternals/downloads/psexec) tool to run the service:

```powershell
psexec -i -s powershell.exe -File "path\to\install-service.ps1"
```

In a SYSTEM context you can run it as shown below:

```powershell
# Run interactively
.\install-service.ps1

# Run non-interactively (asks no questions)
.\install-service.ps1 -NonInteractive -AuthPassPhrase 'MySecretPassPhrase'

# Specify custom options
.\install-service.ps1 -Channel canary -SendHttpJsonUrls 'https://example.com/api' -AuthPassPhrase 'MySecretPassPhrase'
```

## Presets

The [`presets.ini`](./presets.ini) file can be used to configure various variables, such as the password (in clear text) or the report url. See the file for possible variables that can be set. The variables can also be set on the commandline, in which case the presets file is not needed.

## Automated deployment

Have a structure such as:

```
DuplicatiInstaller/
├── install-service.ps1        # Main installer script
├── uninstall-service.ps1      # Uninstall script
├── presets.ini                # Optional preset file for non-interactive values
├── newbackup.json             # Optional preset file for new backups
├── vc_redist.x64.exe          # Optional VC++ redist fallback
└── duplicati-2.1.0.5_stable.msi  # Optional local installer
```

### Intune

1. Use the [Win32 Content Prep Tool](https://learn.microsoft.com/en-us/mem/intune/apps/apps-win32-app-management) to convert this directory into a `.intunewin` package:

```sh
IntuneWinAppUtil.exe -c . -s install-service.ps1 -o output_folder
```

2. Go to Intune Admin Center → Apps → Windows → Add → Windows app (Win32)
3. Upload your .intunewin file
4. Configure the install command

```powershell
powershell.exe -ExecutionPolicy Bypass -File install-service.ps1 -NonInteractive
```

5. Configure the uninstall command

```powershell
powershell.exe -ExecutionPolicy Bypass -File uninstall-service.ps1"
```

6. Configure **Install Behavior**: System
7. Configure the desired OS versions
8. Configure the detection rule
   - Type: Registry
   - Key: HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Duplicati
   - Value: DisplayVersion
   - Operator: Exists

### ManageEngine Endpoint Central

1. Upload the script to the Script Repository
   1. Console ➜ Configurations ▸ Script Repository ▸ Add Script
   2. Script Type: PowerShell
   3. Select install-service.ps1 (and any helper files such as presets.ini, vc_redist.x64.exe, etc.).
   4. Save.
2. Create a Custom Script configuration
   1. Configurations ▸ Add Configuration ▸ Windows Configuration ▸ Custom Script
   2. Computer Configuration (not User).
   3. Give it a name, e.g. Install Duplicati 2.
   4. Script Source: Script Repository ➜ choose the uploaded install-service.ps1.
   5. Arguments (example): `-NonInteractive`
   6. Run As: System user ✅
      (Ensures the script runs in NT AUTHORITY\SYSTEM context, so credentials land in the SYSTEM vault.)
3. Schedule / Deployment
   - Frequency: Once (or During Startup if you prefer).
   - Target: Select the required OU, Domain, or individual computers.
   - Deploy Now.

## Uninstall Script

The `uninstall-service.ps1` script fully removes a Duplicati installation, including optional cleanup steps.

### Usage

```powershell
powershell.exe -ExecutionPolicy Bypass -File uninstall-service.ps1 [-RemoveData] [-RemoveCert] [-RemoveCreds]
```

### Options

| Parameter      | Description                                                                   |
| -------------- | ----------------------------------------------------------------------------- |
| `-RemoveData`  | Deletes all data in `\C:\\ProgramData\\Duplicati` (configuration, logs, etc). |
| ` _RemoveCert` | Removes the TLS stored certificate and deletes `localhost.pfx` if present.    |
| `-RemoveCreds` | Deletes all credentials stored in Credential Manager by the installer.        |

#### What It Does

1. Stops the Duplicati Windows Service (if running)
2. Uninstalls Duplicati using the MSI product name
3. Optionally removes:

   - `C:\\ProgramData\\Duplicati` (with `-RemoveData`)
   - TLS sertificate and key (with `-RemoveCert`)
   - Stored secrets from Credential Manager (with `-RemoveCreds`)
