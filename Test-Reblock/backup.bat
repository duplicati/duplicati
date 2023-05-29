rem Creates a backup from .\Source to .\Destination
rem Run before the other batch files
SET CLI_EXE=..\Duplicati\CommandLine\bin\Debug\Duplicati.CommandLine.exe
%CLI_EXE% help backup

%CLI_EXE% backup "file://.\Destination" ".\Source"
%CLI_EXE% find "file://.\Destination"
pause