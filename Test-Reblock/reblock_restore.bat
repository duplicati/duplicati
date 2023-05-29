rem Uses normal restore to restore the backup with changed block size
rem Run after reblocksize.bat
SET CLI_EXE=..\Duplicati\CommandLine\bin\Debug\Duplicati.CommandLine.exe
rd /s /q Restore2

set /P "passphrase=Enter Passphrase:"
if DEFINED passphrase (
	set passphrase_opt=--passphrase=%passphrase%
	)
if not DEFINED passphrase (
	set passphrase_opt=--no-encryption
	)


%CLI_EXE% restore "file://.\Recover\Reblock" --restore-path=.\Restore2 --overwrite=false %passphrase_opt%

"%ProgramFiles%\Git\git-cmd.exe" --command=usr/bin/bash.exe -l -i -c "diff --binary -r Restore Restore2"

pause