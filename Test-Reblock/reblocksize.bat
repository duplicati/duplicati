rem Change block size of backup in Destination
rem Run after backup.bat
SET REC_EXE=..\Duplicati\CommandLine\RecoveryTool\bin\Debug\Duplicati.CommandLine.RecoveryTool.exe
SET CLI_EXE=..\Duplicati\CommandLine\bin\Debug\Duplicati.CommandLine.exe
rd /s /q Recover
del db.sqlite

set /P "passphrase=Enter Passphrase:"
if DEFINED passphrase (
	set passphrase_opt=--passphrase=%passphrase%
	set encrypt_opt=--encrypt
) else (
	set passphrase_opt=--no-encryption
)

::%REC_EXE% download "file://.\Destination" Recover %passphrase_opt%

::%REC_EXE% index Recover

::%REC_EXE% list Recover

%REC_EXE% reblocksize Destination Recover\Reblock 8 %encrypt_opt% %passphrase_opt% --dblock-size=100mb

%CLI_EXE% repair "file://.\Recover\Reblock" %passphrase_opt% --dbpath=".\db.sqlite"

pause