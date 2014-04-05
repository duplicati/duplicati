@echo off
SET ORIGIN_PATH=%CD%

rmdir /S /Q "bin\Release"

msbuild /property:Configuration=Release /target:Clean ..\Duplicati.sln

echo "Building support tools ..."
msbuild /property:Configuration=Release ..\BuildTools\UpdateVersionStamp\UpdateVersionStamp.sln
msbuild /property:Configuration=Release ..\BuildTools\WixIncludeMake\WixIncludeMake.sln
msbuild /property:Configuration=Release ..\BuildTools\WixProjBuilder\WixProjBuilder.sln

echo "Updating version number ..."
..\BuildTools\UpdateVersionStamp\bin\Release\UpdateVersionStamp.exe --sourcefolder=..

if exist .\buildtag.txt copy .\buildtag.txt ..\Duplicati\Library\License\VersionTag.txt

echo "Building main project ..."
msbuild /property:Configuration=Release ..\Duplicati.sln

echo "Setting up target structure"
mkdir bin
cd bin
mkdir Release
cd Release

mkdir Duplicati
cd Duplicati

mkdir webroot

xcopy /I /Y /E ..\..\..\..\Duplicati\GUI\Duplicati.GUI.TrayIcon\bin\Release\* .
xcopy /I /Y /E ..\..\..\..\Duplicati\Server\webroot\* .\webroot
del *.pdb /Q
del *.mdb /Q
del *.dll.config /Q
xcopy /I /Y "..\..\..\linux help\*" .
del "*.vshost.*" /Q
mkdir Tools
xcopy /I /Y /E ..\..\..\..\Tools .\Tools

if exist .\oem.js copy .\oem.js .\webroot\scripts
if exist .\oem.css copy .\oem.css .\webroot\stylesheets
if exist ..\oem.js copy ..\oem.js .\webroot\scripts
if exist ..\oem.css copy ..\oem.css .\webroot\stylesheets

REM Prepare the config file with version overrides
echo "" > "Duplicati.CommandLine.exe.config"
echo "" > "Duplicati.GUI.TrayIcon.exe.config"
echo "" > "Duplicati.Server.exe.config"
xcopy /Y ..\..\..\AssemblyRedirects.xml "Duplicati.CommandLine.exe.config"
xcopy /Y ..\..\..\AssemblyRedirects.xml "Duplicati.GUI.TrayIcon.exe.config"
xcopy /Y ..\..\..\AssemblyRedirects.xml "Duplicati.Server.exe.config"

cd ..\..\..

echo "Updating WiX list ..."
..\BuildTools\WixIncludeMake\bin\Release\WixIncludeMake.exe --sourcefolder=bin/Release/Duplicati --fileprefix=bin/Release/Duplicati --componentid=DUPLICATIBIN --ignorefilter="*.dll.config:*.mdb:*.log:*.sqlite:*/tempdir:*/control_dir/:*/SVGIcons/:*/lvm-scripts/:*/OSX Icons/"

REM This dll enables Mono on Windows support
xcopy /I /Y "..\thirdparty\SQLite\Bin\sqlite3.dll" "bin\Release\Duplicati"

echo "Building zip version ..."
cd "bin\Release"
"%PROGRAMFILES%\7-zip\7z.exe" a -r "Duplicati.zip" Duplicati
cd "..\.."

echo "Building msi version ..."
..\BuildTools\WixProjBuilder\bin\Release\WixProjBuilder.exe WixInstaller.wixproj
move "bin\Release\Duplicati.msi" "bin\Release\Duplicati.x86.msi"
..\BuildTools\WixProjBuilder\bin\Release\WixProjBuilder.exe --platform=x64 WixInstaller.wixproj
move "bin\Release\Duplicati.msi" "bin\Release\Duplicati.x64.msi"
pause

goto end_of_program

:end_of_program