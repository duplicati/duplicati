rem Uses normal restore from normal backup
rem Run after backup.bat
SET CLI_EXE=..\Duplicati\CommandLine\bin\Debug\Duplicati.CommandLine.exe
rd /s /q Restore


set /P "passphrase=Enter Passphrase:"
if DEFINED passphrase (
	set passphrase_opt=--passphrase=%passphrase%
	)
if not DEFINED passphrase (
	set passphrase_opt=--no-encryption
	)

%CLI_EXE% restore "file://.\Destination" --restore-path=.\Restore %passphrase_opt%
"%ProgramFiles%\Git\git-cmd.exe" --command=usr/bin/bash.exe -l -i -c "diff --binary -r Source Restore"

pause