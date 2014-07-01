echo "2.0_CLI_experimental_`date +%Y-%m-%d`" > Duplicati/License/VersionTag.txt
mono BuildTools/UpdateVersionStamp/bin/Debug/UpdateVersionStamp.exe
xbuild /p:Configuration=Debug Duplicati.sln
rm Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/*.mdb
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/control_dir
rm Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/Duplicati-server.sqlite
rm Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/Duplicati.debug.log
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/updates
cp -R Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/* ~/Dropbox/Duplicati/2.0/2.0_snapshot/
cp -R Duplicati/Server/webroot ~/Dropbox/Duplicati/2.0/2.0_snapshot/
