rem Uses recover to restore the backup with changed block size
rem Run after reblocksize.bat
SET REC_EXE=..\Duplicati\CommandLine\RecoveryTool\bin\Debug\Duplicati.CommandLine.RecoveryTool.exe
rd /s /q Recover\Reblock\Restore

%REC_EXE% index Recover\Reblock

%REC_EXE% list Recover\Reblock

%REC_EXE% restore Recover\Reblock --targetpath=.\Restore\

"%ProgramFiles%\Git\git-cmd.exe" --command=usr/bin/bash.exe -l -i -c "diff --binary -r Source Recover/Reblock/Restore"

pause