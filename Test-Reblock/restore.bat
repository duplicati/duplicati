rem Uses normal restore from normal backup
rem Run after backup.bat
SET CLI_EXE=..\Duplicati\CommandLine\bin\Debug\Duplicati.CommandLine.exe
rd /s /q Restore

%CLI_EXE% restore "file://.\Destination" --restore-path=.\Restore --overwrite=false --no-encryption=true
"%ProgramFiles%\Git\git-cmd.exe" --command=usr/bin/bash.exe -l -i -c "diff --binary -r Source Restore"

pause