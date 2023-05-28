: Uses normal restore to restore the backup with changed block size
: Run after reblocksize.bat
SET CLI_EXE=..\Duplicati\CommandLine\bin\Debug\Duplicati.CommandLine.exe
rd /s /q Restore

%CLI_EXE% restore "file://.\Recover\Reblock" --restore-path=.\Restore --overwrite=false --passphrase=asdf

"%ProgramFiles%\Git\git-cmd.exe" --command=usr/bin/bash.exe -l -i -c "diff --binary -r Source Restore"

pause