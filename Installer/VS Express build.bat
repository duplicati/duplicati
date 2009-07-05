xcopy /Y "..\thirdparty\SQLite\Dll for .Net\System.Data.SQLite.dll" "..\Duplicati\GUI\bin\Release\System.Data.SQLite.dll"
WixProjBuilder.exe WixInstaller.wixproj
move /Y bin\Release\Duplicati.msi "bin\Release\Duplicati x86.msi"

xcopy /Y "..\thirdparty\SQLite\Dll for .Net\x64\System.Data.SQLite.dll" "..\Duplicati\GUI\bin\Release\System.Data.SQLite.dll"
WixProjBuilder.exe WixInstaller.wixproj
move /Y bin\Release\Duplicati.msi "bin\Release\Duplicati x64.msi"

pause