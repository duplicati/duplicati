rem Uses normal restore to restore the backup with changed block size
rem Run after reblocksize.bat
SET CLI_EXE=..\Duplicati\CommandLine\bin\Debug\Duplicati.CommandLine.exe
rd /s /q Restore2

%CLI_EXE% restore "file://.\Recover\Reblock" --restore-path=.\Restore2 --overwrite=false --passphrase=asdf
:: %CLI_EXE% restore "file://.\Recover\Reblock" --restore-path=.\Restore --overwrite=false --no-encryption

"%ProgramFiles%\Git\git-cmd.exe" --command=usr/bin/bash.exe -l -i -c "diff --binary -r Restore Restore2"

pause