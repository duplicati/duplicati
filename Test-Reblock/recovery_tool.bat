rem Use recovery tool to index recover files
rem Run after backup.bat
SET REC_EXE=..\Duplicati\CommandLine\RecoveryTool\bin\Debug\Duplicati.CommandLine.RecoveryTool.exe
rd /s /q Recover

%REC_EXE% download "file://.\Destination" Recover --no-encryption=true

%REC_EXE% index Recover

%REC_EXE% list Recover
pause