@echo off
mkdir Duplicity
cd Duplicity
cd
del * /Q
xcopy /I /Y ..\..\Duplicati\GUI\Bin\Release\* .
del *.pdb /Q
xcopy /I /Y ..\..\thirdparty\SQLite\Bin\sqlite-3.6.12.so .
move sqlite-3.6.12.so libsqlite3.so.0
xcopy /I /Y "..\..\thirdparty\SQLite\Bin\linux howto.txt" .
move "linux howto.txt" linux-sqlite-readme.txt
xcopy /I /Y "..\..\thirdparty\SQLite\Dll for .Net\ManagedOnly\System.Data.SQLite.dll" .
pause