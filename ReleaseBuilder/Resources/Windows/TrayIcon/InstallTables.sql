-- Post-wixl MSI table customizations for the TrayIcon installer.
--
-- The file is processed by Command.CreatePackage.cs after wixl produces the
-- MSI: each non-empty, non-comment line is run as a single `msibuild -q`
-- query. Statements are executed in order; lines beginning with `--` and
-- blank lines are ignored.
--
-- This enables certain customizations that wixl cannot handle natively.

-- ---------------------------------------------------------------------------
-- MsiLockPermissionsEx: ACL for the install dir.

-- Replaces the WiX <util:PermissionEx> element on the install
-- directory that wixl cannot parse.
-- ---------------------------------------------------------------------------
CREATE TABLE `MsiLockPermissionsEx` (`MsiLockPermissionsEx` CHAR(72) NOT NULL, `LockObject` CHAR(72) NOT NULL, `Table` CHAR(32) NOT NULL, `SDDLText` CHAR(255) NOT NULL, `Condition` CHAR(255) PRIMARY KEY `MsiLockPermissionsEx`, `LockObject`, `Table`)
INSERT INTO `MsiLockPermissionsEx` (`MsiLockPermissionsEx`, `LockObject`, `Table`, `SDDLText`) VALUES ('SecureFolderACLs', 'INSTALLLOCATION', 'CreateFolder', 'D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRFX;;;BU)')

-- ---------------------------------------------------------------------------
-- RemoveRegistry: deletes the BootstrapApplied sentinel value on install so
-- a stale value from a previous install does not get re-picked-up by the
-- service. Replaces the WiX <RemoveRegistryValue Action="removeOnInstall"/>
-- inside DuplicatiServiceFeatureValuesComponent (wixl does not emit this).
-- ---------------------------------------------------------------------------
CREATE TABLE `RemoveRegistry` (`RemoveRegistry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `RemoveRegistry`)

-- ---------------------------------------------------------------------------
-- MoveFile: copies preload.json from the directory containing the MSI
-- (resolved into PRELOAD_SOURCE by SetPreloadSource) into INSTALLLOCATION
-- when the user passes INSTALL_PRELOAD=true on the msiexec command line.
-- The MoveFile row is keyed to the always-installed Duplicati.GUI.TrayIcon.exe
-- component so it ships with the rest of the binaries and follows their
-- install/uninstall lifecycle. Options=0 means copy (not move). Replaces the
-- WiX <CopyFile> element that wixl cannot parse on a Component without a
-- <File> child.
--
-- The standard MoveFiles action that processes this table is scheduled
-- further below in this file.
-- ---------------------------------------------------------------------------
CREATE TABLE `MoveFile` (`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `SourceName` CHAR(255) LOCALIZABLE, `DestName` CHAR(255) LOCALIZABLE, `SourceFolder` CHAR(72), `DestFolder` CHAR(72) NOT NULL, `Options` SHORT NOT NULL PRIMARY KEY `FileKey`)
INSERT INTO `MoveFile` (`FileKey`, `Component_`, `SourceName`, `DestName`, `SourceFolder`, `DestFolder`, `Options`) VALUES ('CopyPreloadJson', 'Duplicati.GUI.TrayIcon.exe', 'preload.json', 'preload.json', 'PRELOAD_SOURCE', 'INSTALLLOCATION', 0)

-- ---------------------------------------------------------------------------
-- RemoveFile: removes preload.json from INSTALLLOCATION on uninstall
-- (InstallMode=2 = msidbRemoveFileInstallModeOnRemove). The RemoveFile
-- table is already created by wixl so we only INSERT here. Replaces the
-- WiX <RemoveFile> element under the preload component.
-- ---------------------------------------------------------------------------
INSERT INTO `RemoveFile` (`FileKey`, `Component_`, `FileName`, `DirProperty`, `InstallMode`) VALUES ('RemovePreloadJson', 'Duplicati.GUI.TrayIcon.exe', 'preload.json', 'INSTALLLOCATION', 2)

-- ---------------------------------------------------------------------------
-- InstallExecuteSequence: schedule the standard ResolveSource and MoveFiles
-- actions so the MoveFile row above is actually processed. wixl does not
-- auto-insert these standard actions because their respective tables are
-- empty at wixl build time. Both are gated by INSTALL_PRELOAD="true" so they
-- only fire when the user explicitly opts in.
--   ResolveSource @ 850 - populates [SourceDir] before SetPreloadSource
--     consumes it (SetPreloadSource itself is declared in the WXS at ~1001
--     via <Custom Action="SetPreloadSource" After="CostFinalize">).
--   MoveFiles     @ 3700 - canonical position between RemoveFiles (3500)
--     and InstallFiles (4000).
-- ---------------------------------------------------------------------------
INSERT INTO `InstallExecuteSequence` (`Action`, `Condition`, `Sequence`) VALUES ('ResolveSource', 'INSTALL_PRELOAD="true" AND NOT Installed', 850)
INSERT INTO `InstallExecuteSequence` (`Action`, `Condition`, `Sequence`) VALUES ('MoveFiles', 'INSTALL_PRELOAD="true"', 3700)

-- ---------------------------------------------------------------------------
-- MsiServiceConfig: register the Duplicati service as Automatic (Delayed
-- Start). Replaces the WiX <ServiceConfig DelayedAutoStart="yes"/> element (a
-- child of <ServiceInstall>) that wixl 0.106 cannot parse. On Windows the real
-- WiX toolchain emits this table natively from the WXS <ServiceConfig> element.
--
--   Name        - the service name (matches ServiceInstall/@Name in the WXS).
--   Event       - 7 = msidbServiceConfigEventInstall (1) | ...Reinstall (2) |
--                 ...Uninstall (4); apply the config on install and reinstall
--                 (the uninstall bit is harmless as there is nothing to undo).
--   ConfigType  - 3 = SERVICE_CONFIG_DELAYED_AUTO_START_INFO.
--   Argument    - '1' enables delayed auto-start (only affects auto services).
--   Component_  - keyed to DuplicatiWindowsServiceComponent, which is only
--                 installed when the service feature is selected
--                 (DUPLICATI_SERVICE_SELECTED). MsiConfigureServices only acts
--                 on rows whose component is being installed, so the delayed
--                 start config applies exactly when the service is installed.
--
-- The standard MsiConfigureServices action that processes this table is
-- scheduled below.
-- ---------------------------------------------------------------------------
CREATE TABLE `MsiServiceConfig` (`MsiServiceConfig` CHAR(72) NOT NULL, `Name` CHAR(255) NOT NULL, `Event` INT NOT NULL, `ConfigType` INT NOT NULL, `Argument` CHAR(255), `Component_` CHAR(72) NOT NULL PRIMARY KEY `MsiServiceConfig`)
INSERT INTO `MsiServiceConfig` (`MsiServiceConfig`, `Name`, `Event`, `ConfigType`, `Argument`, `Component_`) VALUES ('DelayStartSvc', 'Duplicati', 7, 3, '1', 'DuplicatiWindowsServiceComponent')

-- ---------------------------------------------------------------------------
-- InstallExecuteSequence: schedule the standard MsiConfigureServices action so
-- the MsiServiceConfig row above is processed. wixl does not auto-insert it
-- because the table is empty at wixl build time. 5850 is just after the
-- standard InstallServices (5800), so the service exists when it is configured.
-- ---------------------------------------------------------------------------
INSERT INTO `InstallExecuteSequence` (`Action`, `Condition`, `Sequence`) VALUES ('MsiConfigureServices', '', 5850)
