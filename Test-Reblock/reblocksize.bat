rem Change block size of backup in Destination
rem Run after backup.bat
SET REC_EXE=..\Duplicati\CommandLine\RecoveryTool\bin\Debug\Duplicati.CommandLine.RecoveryTool.exe
SET CLI_EXE=..\Duplicati\CommandLine\bin\Debug\Duplicati.CommandLine.exe
rd /s /q Recover
del db.sqlite

set /P "passphrase=Enter Passphrase:"
:: %REC_EXE% download "file://.\Destination" Recover --no-encryption=true
%REC_EXE% download "file://.\Destination" Recover --passphrase=%passphrase%

%REC_EXE% index Recover

%REC_EXE% list Recover

%REC_EXE% reblocksize Recover Recover\Reblock 8 --encrypt --passphrase=asdf --dblock-size=100mb
:: %REC_EXE% reblocksize Recover Recover\Reblock 8

%CLI_EXE% repair "file://.\Recover\Reblock" --passphrase=asdf --dbpath=".\db.sqlite"

pause