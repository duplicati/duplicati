@echo off
cd bin\Release
mkdir Duplicati
cd Duplicati
del * /Q
xcopy /I /Y /E ..\..\..\..\Duplicati\GUI\Bin\Release\* .
del *.pdb /Q
xcopy /I /Y ..\..\..\..\thirdparty\SQLite\Bin\sqlite-3.6.12.so .
move sqlite-3.6.12.so libsqlite3.so.0
xcopy /I /Y "..\..\..\linux help\*" .
del "*.vshost.*" /Q
xcopy /I /Y ..\..\..\..\Duplicati\GUI\StartDuplicati.sh .

cd ..
del ..\Duplicati.msi
del ..\Duplicati.zip

"%PROGRAMFILES%\7-zip\7z.exe" a -r Duplicati.zip Duplicati

cd ..\..

paraffin -dir bin\Release\Duplicati -custom DUPLICATIBIN -dirref ProgramFilesFolder -ext .pdb -alias bin\Release\Duplicati -guids incBinFiles.wxs 

WixProjBuilder.exe WixInstaller.wixproj
pause