@echo off
cd bin\Release
mkdir Duplicati
cd Duplicati
cd
del * /Q
xcopy /I /Y ..\..\..\..\Duplicati\GUI\Bin\Release\* .
del *.pdb /Q
xcopy /I /Y ..\..\..\..\thirdparty\SQLite\Bin\sqlite-3.6.12.so .
move sqlite-3.6.12.so libsqlite3.so.0
xcopy /I /Y "..\..\..\linux help\*" .
move "linux-sqlite-readme.txt" "sqlite-readme.txt"
move "linux-readme.txt" "README.txt"
del "System.Data.SQLite.dll" /Q
del "*.vshost.*" /Q
xcopy /I /Y "..\..\..\..\thirdparty\SQLite\Dll for .Net\ManagedOnly\System.Data.SQLite.dll" .
xcopy /I /Y ..\..\..\..\Duplicati\GUI\StartDuplicati.sh .
pause