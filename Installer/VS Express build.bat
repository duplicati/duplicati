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
xcopy /Y ..\..\..\..\Duplicati\GUI\StartDuplicati.sh .

REM This dll enables Mono on Windows support
xcopy /I /Y ..\..\..\..\thirdparty\SQLite\Bin\sqlite3.dll .

REM Prepare the config file with version overrides
del "Duplicati.exe.config" /Q
del "Duplicati.CommandLine.exe.config" /Q
echo "" > "Duplicati.CommandLine.exe.config"
echo "" > "Duplicati.exe.config"
xcopy /Y ..\..\..\AssemblyRedirects.xml "Duplicati.exe.config"
xcopy /Y ..\..\..\AssemblyRedirects.xml "Duplicati.CommandLine.exe.config"

REM Build translations : *TODO* Make this pick up output from the "compiled" folder automatically
REM ..\..\..\..\Duplicati\Localization\LocalizationTool.exe CLEAN
del "..\..\..\..\Duplicati\Localization\compiled\*" /Q
..\..\..\..\Duplicati\Localization\LocalizationTool.exe UPDATE
..\..\..\..\Duplicati\Localization\LocalizationTool.exe BUILD
xcopy /I /Y /E ..\..\..\..\Duplicati\Localization\Compiled\fr-FR fr-FR
xcopy /I /Y /E ..\..\..\..\Duplicati\Localization\Compiled\pt-BR pt-BR

cd ..
del ..\Duplicati.msi /Q
del ..\Duplicati.x86.msi /Q
del ..\Duplicati.x64.msi /Q
del ..\Duplicati.zip /Q


"%PROGRAMFILES%\7-zip\7z.exe" a -r Duplicati.zip Duplicati

cd ..\..

REM Create incBinFiles.wxs if required
REM Does NOT set the FILE_DUPLICATI_MAIN_EXE id on the main file, which is required to build the MSI
if not exist incBinFiles.wxs paraffin -dir bin\Release\Duplicati -groupname DUPLICATIBIN -dirref INSTALLLOCATION -ext .pdb -ext .0 -alias bin\Release\Duplicati -norootdirectory -multiple -Win64var "$(var.Win64)" incBinFiles.wxs 

REM Update version
if exist incBinFiles.PARAFFIN del incBinFiles.PARAFFIN
paraffin -update incBinFiles.wxs
if exist incBinFiles.PARAFFIN xcopy /I /Y incBinFiles.PARAFFIN incBinFiles.wxs
if exist incBinFiles.PARAFFIN del incBinFiles.PARAFFIN

WixProjBuilder.exe --wixpath="C:\Program Files (x86)\Windows Installer XML v3\bin" WixInstaller.wixproj
move "bin\Release\Duplicati.msi" "bin\Release\Duplicati.x86.msi"
WixProjBuilder.exe --wixpath="C:\Program Files (x86)\Windows Installer XML v3\bin" --platform=x64 WixInstaller.wixproj
move "bin\Release\Duplicati.msi" "bin\Release\Duplicati.x64.msi"
pause