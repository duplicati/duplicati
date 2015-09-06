@echo off

call "%VS120COMNTOOLS%vsvars32.bat"

rmdir /s /q Duplicati\Server\webroot

7z x -oDuplicati\Server %1 webroot

rmdir /s /q Duplicati\GUI\Duplicati.GUI.TrayIcon\bin\Release
mkdir Duplicati\GUI\Duplicati.GUI.TrayIcon\bin\Release

7z x -oDuplicati\GUI\Duplicati.GUI.TrayIcon\bin\Release -x!webroot %1

cd Installer

rmdir /s /q obj
rmdir /s /q bin

msbuild /property:Configuration=Release

cd ..

copy Installer\bin\x64\Release\Duplicati.msi Duplicati.msi
