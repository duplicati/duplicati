rem Creates a backup from .\Source to .\Destination
rem Run before the other batch files
SET CLI_EXE=..\Duplicati\CommandLine\bin\Debug\Duplicati.CommandLine.exe

set /P "passphrase=Enter Passphrase:"
if DEFINED passphrase (
	set passphrase_opt=--passphrase=%passphrase%
	)
if not DEFINED passphrase (
	set passphrase_opt=--no-encryption
	)

%CLI_EXE% backup "file://.\Destination" ".\Source" --dbpath=.\backup.sqlite %passphrase_opt%
%CLI_EXE% find "file://.\Destination" --dbpath=.\backup.sqlite
pause