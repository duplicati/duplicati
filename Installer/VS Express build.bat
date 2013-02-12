@echo off
SET PARAFFIN_EXE=%CD%\paraffin.exe
SET ORIGIN_PATH=%CD%

if exist "%PARAFFIN_EXE%" goto paraffin_found
echo *****************************************************
echo Missing Paraffin.exe, download from here: 
echo http://www.wintellect.com/CS/files/folders/8198/download.aspx
echo *****************************************************
pause
goto end_of_program

REM TODO: Consider rewriting this in Python so it becomes readable :(

REM Should also write a new Paraffin replacement so we can distribute it,
REM  it should be fairly simple and not support everything Paraffin does

:paraffin_found

rmdir /S /Q "bin\Release"

mkdir bin
cd bin
mkdir Release
cd Release


mkdir Duplicati
cd Duplicati

xcopy /I /Y /E ..\..\..\..\Duplicati\GUI\Bin\Release\* .
del *.pdb /Q
xcopy /I /Y "..\..\..\linux help\*" .
del "*.vshost.*" /Q
xcopy /Y ..\..\..\..\Duplicati\GUI\StartDuplicati.sh .
mkdir Tools
xcopy /I /Y /E ..\..\..\..\Tools .\Tools

REM Prepare the config file with version overrides
echo "" > "Duplicati.CommandLine.exe.config"
echo "" > "Duplicati.exe.config"
xcopy /Y ..\..\..\AssemblyRedirects.xml "Duplicati.exe.config"
xcopy /Y ..\..\..\AssemblyRedirects.xml "Duplicati.CommandLine.exe.config"

cd ..\..\..

REM Build translations
cd "..\Duplicati\Localization"
rmdir /S /Q compiled

LocalizationTool.exe update
LocalizationTool.exe webupdate
for %%d in (".\*.csv") do call :reimportcsv %%d

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

REM Copy in translation files
xcopy /I /Y /E "..\Duplicati\Localization\compiled\*" "bin\Release\Duplicati"

REM Support for linux with old SQLite binaries
xcopy /I /Y "..\thirdparty\SQLite\Bin\sqlite-3.6.12.so" "bin\Release\Duplicati"
move sqlite-3.6.12.so libsqlite3.so.0

REM This dll enables Mono on Windows support
xcopy /I /Y "..\thirdparty\SQLite\Bin\sqlite3.dll" "bin\Release\Duplicati"

REM Build zip version
cd "bin\Release"
"%PROGRAMFILES%\7-zip\7z.exe" a -r "Duplicati.zip" Duplicati
cd "..\.."

WixProjBuilder.exe WixInstaller.wixproj
move "bin\Release\Duplicati.msi" "bin\Release\Duplicati.x86.msi"
WixProjBuilder.exe --platform=x64 WixInstaller.wixproj
move "bin\Release\Duplicati.msi" "bin\Release\Duplicati.x64.msi"
pause

goto end_of_program

:reimportcsv
set LANG_NAME=

REM Read the second value delimited by . (the filename is .\report.en-US.csv)
for /f "tokens=2 delims=." %%a in ("%1") do set LANG_NAME=%%a
LocalizationTool.exe import %LANG_NAME% %1

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