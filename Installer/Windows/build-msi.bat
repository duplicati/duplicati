@echo off

IF NOT EXIST "%1" (
echo File not found, please supply a zip file with the build as the first argument
goto EXIT
)


call "%VS120COMNTOOLS%vsvars32.bat"

rmdir /s /q Duplicati
del /q Duplicati.msi
del /q Duplicati-32bit.msi

7z x -oDuplicati %1

rmdir /s /q obj
rmdir /s /q bin

msbuild /property:Configuration=Release /property:Platform=x64
move bin\x64\Release\Duplicati.msi Duplicati.msi

msbuild /property:Configuration=Release /property:Platform=x86
move bin\x86\Release\Duplicati.msi Duplicati-32bit.msi

rmdir /s /q Duplicati

:EXIT