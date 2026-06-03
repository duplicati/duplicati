-- Post-wixl MSI table customizations for the Agent installer.
--
-- The file is processed by Command.CreatePackage.cs after wixl produces the
-- MSI: each non-empty, non-comment line is run as a single `msibuild -q`
-- query. Statements are executed in order; lines beginning with `--` and
-- blank lines are ignored.
--
-- Why this exists: wixl 0.106 does not understand WiX elements such as
-- <util:PermissionEx> or <CopyFile> (on a Component) - it silently drops
-- them or fails to build. On Windows the real WiX toolchain emits the
-- corresponding tables natively from the WXS sources; on Linux/macOS we
-- have to author the rows directly. Statements that touch tables wixl
-- already emits (RemoveFile, InstallExecuteSequence) must use INSERT only,
-- never CREATE, because `msibuild -q "CREATE TABLE foo"` against an
-- existing table fails.

-- ---------------------------------------------------------------------------
-- MsiLockPermissionsEx: ACLs for the install dir. Replaces the WiX
-- <util:PermissionEx> element on SecureInstallDirComp that wixl cannot parse.
-- ---------------------------------------------------------------------------
CREATE TABLE `MsiLockPermissionsEx` (`MsiLockPermissionsEx` CHAR(72) NOT NULL, `LockObject` CHAR(72) NOT NULL, `Table` CHAR(32) NOT NULL, `SDDLText` CHAR(255) NOT NULL, `Condition` CHAR(255) PRIMARY KEY `MsiLockPermissionsEx`, `LockObject`, `Table`)
INSERT INTO `MsiLockPermissionsEx` (`MsiLockPermissionsEx`, `LockObject`, `Table`, `SDDLText`) VALUES ('SecureFolderACLs', 'INSTALLLOCATION', 'CreateFolder', 'D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRFX;;;BU)')

-- ---------------------------------------------------------------------------
-- MoveFile: copies preload.json from the directory containing the MSI
-- (resolved into PRELOAD_SOURCE by SetPreloadSource) into INSTALLLOCATION
-- when the user passes INSTALL_PRELOAD=true on the msiexec command line.
-- The MoveFile row is keyed to the always-installed Duplicati.Agent.exe
-- component so it ships with the rest of the binaries and follows their
-- install/uninstall lifecycle. Options=0 means copy (not move). Replaces
-- the WiX <CopyFile> element that wixl cannot parse on a Component without
-- a <File> child.
--
-- The standard MoveFiles action that processes this table is scheduled
-- further below in this file.
-- ---------------------------------------------------------------------------
CREATE TABLE `MoveFile` (`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `SourceName` CHAR(255) LOCALIZABLE, `DestName` CHAR(255) LOCALIZABLE, `SourceFolder` CHAR(72), `DestFolder` CHAR(72) NOT NULL, `Options` SHORT NOT NULL PRIMARY KEY `FileKey`)
INSERT INTO `MoveFile` (`FileKey`, `Component_`, `SourceName`, `DestName`, `SourceFolder`, `DestFolder`, `Options`) VALUES ('CopyPreloadJson', 'Duplicati.Agent.exe', 'preload.json', 'preload.json', 'PRELOAD_SOURCE', 'INSTALLLOCATION', 0)

-- ---------------------------------------------------------------------------
-- RemoveFile: removes preload.json from INSTALLLOCATION on uninstall
-- (InstallMode=2 = msidbRemoveFileInstallModeOnRemove). The RemoveFile
-- table is already created by wixl so we only INSERT here.
-- ---------------------------------------------------------------------------
INSERT INTO `RemoveFile` (`FileKey`, `Component_`, `FileName`, `DirProperty`, `InstallMode`) VALUES ('RemovePreloadJson', 'Duplicati.Agent.exe', 'preload.json', 'INSTALLLOCATION', 2)

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
