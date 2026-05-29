-- Post-wixl MSI table customizations for the TrayIcon installer.
--
-- The file is processed by Command.CreatePackage.cs after wixl produces the
-- MSI: each non-empty, non-comment line is run as a single `msibuild -q`
-- query. Statements are executed in order; lines beginning with `--` and
-- blank lines are ignored.
--
-- This enables certain customizations that wixl cannot handle natively.

-- ---------------------------------------------------------------------------
-- MsiLockPermissionsEx: ACLs for the install dir and the protected
-- Service / ServiceState registry keys created by
-- DuplicatiServiceMarkerComponent. Replaces the WiX <util:PermissionEx>
-- elements that wixl cannot parse.
-- ---------------------------------------------------------------------------
CREATE TABLE `MsiLockPermissionsEx` (`MsiLockPermissionsEx` CHAR(72) NOT NULL, `LockObject` CHAR(72) NOT NULL, `Table` CHAR(32) NOT NULL, `SDDLText` CHAR(255) NOT NULL, `Condition` CHAR(255) PRIMARY KEY `MsiLockPermissionsEx`, `LockObject`, `Table`)
INSERT INTO `MsiLockPermissionsEx` (`MsiLockPermissionsEx`, `LockObject`, `Table`, `SDDLText`) VALUES ('SecureFolderACLs', 'INSTALLLOCATION', 'CreateFolder', 'D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRFX;;;BU)')
INSERT INTO `MsiLockPermissionsEx` (`MsiLockPermissionsEx`, `LockObject`, `Table`, `SDDLText`) VALUES ('SecureServiceRegKey', 'ServiceInstalledMarker', 'Registry', 'D:P(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)')
INSERT INTO `MsiLockPermissionsEx` (`MsiLockPermissionsEx`, `LockObject`, `Table`, `SDDLText`) VALUES ('OpenServiceStateRegKey', 'ServiceStateInstalledMarker', 'Registry', 'D:P(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)(A;OICI;KR;;;AU)(A;OICI;KR;;;BU)')

-- ---------------------------------------------------------------------------
-- RemoveRegistry: deletes the BootstrapApplied sentinel value on install so
-- a stale value from a previous install does not get re-picked-up by the
-- service. Replaces the WiX <RemoveRegistryValue Action="removeOnInstall"/>
-- inside DuplicatiServiceMarkerComponent (wixl does not emit this).
-- ---------------------------------------------------------------------------
CREATE TABLE `RemoveRegistry` (`RemoveRegistry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `RemoveRegistry`)
INSERT INTO `RemoveRegistry` (`RemoveRegistry`, `Root`, `Key`, `Name`, `Component_`) VALUES ('RemoveStaleBootstrapApplied', 2, 'SOFTWARE\DuplicatiTeam\Duplicati\ServiceState', 'BootstrapApplied', 'DuplicatiServiceMarkerComponent')

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
