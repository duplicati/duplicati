@echo off
SET PARAFFIN_EXE=%CD%\paraffin.exe
SET ORIGIN_PATH=%CD%

if exist "%PARAFFIN_EXE%" goto paraffin_found
echo *****************************************************
echo Missing Paraffin.exe, download from here: 
echo http://www.wintellect.com/CS/files/folders/sample_files/entry7420.aspx
echo *****************************************************
pause
goto end_of_program

:paraffin_found

rmdir /S /Q "bin\Release\Duplicati"

mkdir bin
cd bin
mkdir Release
cd Release


mkdir Duplicati
cd Duplicati

xcopy /I /Y /E ..\..\..\..\Duplicati\GUI\Bin\Release\* .
del *.pdb /Q
xcopy /I /Y ..\..\..\..\thirdparty\SQLite\Bin\sqlite-3.6.12.so .
move sqlite-3.6.12.so libsqlite3.so.0
xcopy /I /Y "..\..\..\linux help\*" .
del "*.vshost.*" /Q
xcopy /Y ..\..\..\..\Duplicati\GUI\StartDuplicati.sh .
mkdir Tools
xcopy /I /Y /E ..\..\..\..\Tools .\Tools

REM This dll enables Mono on Windows support
xcopy /I /Y ..\..\..\..\thirdparty\SQLite\Bin\sqlite3.dll .

REM Prepare the config file with version overrides
del "Duplicati.exe.config" /Q
del "Duplicati.CommandLine.exe.config" /Q
echo "" > "Duplicati.CommandLine.exe.config"
echo "" > "Duplicati.exe.config"
xcopy /Y ..\..\..\AssemblyRedirects.xml "Duplicati.exe.config"
xcopy /Y ..\..\..\AssemblyRedirects.xml "Duplicati.CommandLine.exe.config"

cd ..
del .\Duplicati.msi /Q
del .\Duplicati.x86.msi /Q
del .\Duplicati.x64.msi /Q
del .\Duplicati.zip /Q

cd ..\..

REM Build translations
cd "..\Duplicati\Localization"
rmdir /S /Q compiled

REM TODO: Make this dynamic with report.*.csv
LocalizationTool.exe update

LocalizationTool.exe import da-DK report.da-DK.csv
LocalizationTool.exe import fr-FR report.fr-FR.csv
LocalizationTool.exe import pt-BR report.pt-BR.csv
LocalizationTool.exe import de-DE report.de-DE.csv
LocalizationTool.exe import es-ES report.es-ES.csv

LocalizationTool.exe update
LocalizationTool.exe build
for /D %%d in ("compiled\*") do call :langbuild %%d

cd "..\..\Installer"

REM Create incBinFiles.wxs if required
REM Does NOT set the FILE_DUPLICATI_MAIN_EXE id on the main file, which is required to build the MSI
if not exist incBinFiles.wxs "%PARAFFIN_EXE%" -dir bin\Release\Duplicati -groupname DUPLICATIBIN -dirref INSTALLLOCATION -ext .pdb -ext .0 -alias bin\Release\Duplicati -norootdirectory -multiple -Win64var "$(var.Win64)" incBinFiles.wxs 

REM Update version
if exist incBinFiles.PARAFFIN del incBinFiles.PARAFFIN
"%PARAFFIN_EXE%" -update incBinFiles.wxs
if exist incBinFiles.PARAFFIN xcopy /I /Y incBinFiles.PARAFFIN incBinFiles.wxs
if exist incBinFiles.PARAFFIN del incBinFiles.PARAFFIN

xcopy /I /Y /E "..\Duplicati\Localization\compiled\*" "bin\Release\Duplicati"
cd "bin\Release"
"%PROGRAMFILES%\7-zip\7z.exe" a -r "Duplicati.zip" Duplicati
cd "..\.."

WixProjBuilder.exe --wixpath="C:\Program Files (x86)\Windows Installer XML v3\bin" WixInstaller.wixproj
move "bin\Release\Duplicati.msi" "bin\Release\Duplicati.x86.msi"
WixProjBuilder.exe --wixpath="C:\Program Files (x86)\Windows Installer XML v3\bin" --platform=x64 WixInstaller.wixproj
move "bin\Release\Duplicati.msi" "bin\Release\Duplicati.x64.msi"
pause

goto end_of_program

:langbuild
set FOLDERNAME=

for /f "tokens=*" %%a in ("%1") do set FOLDERNAME=%%~na

SET LANG_WXS_NAME=%ORIGIN_PATH%\incLocFiles.%FOLDERNAME%.wxs
SET LANG_PRF_NAME=%ORIGIN_PATH%\incLocFiles.%FOLDERNAME%.PARAFFIN

if not exist "%LANG_WXS_NAME%" "%PARAFFIN_EXE%" -dir %1 -alias ..\Duplicati\Localization\%1 -groupname DUPLICATI_LANG_%FOLDERNAME% -dirref INSTALLLOCATION "%LANG_WXS_NAME%" -Win64var "$(var.Win64)" -multiple -ext .pdb -direXclude .svn
if exist "%LANG_PRF_NAME%" del "%LANG_PRF_NAME%"
"%PARAFFIN_EXE%" -update "%ORIGIN_PATH%\incLocFiles.%FOLDERNAME%.wxs"
if exist "%LANG_PRF_NAME%" xcopy /I /Y "%LANG_PRF_NAME%" "%LANG_WXS_NAME%"
if exist "%LANG_PRF_NAME%" del "%LANG_PRF_NAME%"

goto end_of_program

:end_of_program