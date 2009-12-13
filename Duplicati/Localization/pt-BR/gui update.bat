@echo off

echo Processing updates, please wait ...

set FOLDERNAME=
for /f "tokens=*" %%a in ("%CD%") do set FOLDERNAME=%%~na

..\LocalizationTool.exe update %FOLDERNAME%
..\LocalizationTool.exe guiupdate %FOLDERNAME%