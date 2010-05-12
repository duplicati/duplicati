@echo off
mkdir bin
cd bin
mkdir Release
cd Release
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

REM Create version
REM paraffin -dir bin\Release\Duplicati -groupname DUPLICATIBIN -dirref INSTALLLOCATION -ext .pdb -ext .0 -alias bin\Release\Duplicati -norootdirectory -multiple  incBinFiles.wxs 

REM Update version
if exist incBinFiles.PARAFFIN del incBinFiles.PARAFFIN
paraffin -update incBinFiles.wxs
if exist incBinFiles.PARAFFIN xcopy /I /Y incBinFiles.PARAFFIN incBinFiles.wxs
if exist incBinFiles.PARAFFIN del incBinFiles.PARAFFIN

WixProjBuilder.exe --wixpath="C:\Program Files (x86)\Windows Installer XML v3\bin" WixInstaller.wixproj
pause