RELEASE_TIMESTAMP=`date +%Y-%m-%d`

RELEASE_INC_VERSION=`cat Updates/debug_version.txt`
RELEASE_INC_VERSION=$((RELEASE_INC_VERSION+0))

RELEASE_NAME=2.0_CLI_experimental_${RELEASE_TIMESTAMP}
RELEASE_CHANGEINFO=`cat Updates/debug_changeinfo.txt`
RELEASE_VERSION="2.0.0.${RELEASE_INC_VERSION}"

echo "${RELEASE_NAME}" > Duplicati/License/VersionTag.txt
cp "Updates/debug_urls.txt"  Duplicati/Library/AutoUpdater/AutoUpdateURL.txt
cp "Updates/debug_key.txt"  Duplicati/Library/AutoUpdater/AutoUpdateSignKey.txt

mono BuildTools/UpdateVersionStamp/bin/Debug/UpdateVersionStamp.exe --version="${RELEASE_VERSION}"
xbuild /p:Configuration=Debug Duplicati.sln

rm Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/*.mdb
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/control_dir
rm Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/Duplicati-server.sqlite
rm Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/Duplicati.debug.log
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/updates
cp -R Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/* ~/Dropbox/Duplicati/2.0/2.0_snapshot/
cp -R Duplicati/Server/webroot ~/Dropbox/Duplicati/2.0/2.0_snapshot/

mono BuildTools/UpdateVersionStamp/bin/Debug/UpdateVersionStamp.exe --version="2.0.0.7"
