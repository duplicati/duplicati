RELEASE_TIMESTAMP=`date +%Y-%m-%d`

RELEASE_INC_VERSION=`cat Updates/debug_version.txt`
RELEASE_INC_VERSION=$((RELEASE_INC_VERSION+1))

RELEASE_NAME=2.0_CLI_experimental_${RELEASE_TIMESTAMP}
RELEASE_CHANGEINFO=`cat Updates/debug_changeinfo.txt`
RELEASE_VERSION="2.0.0.${RELEASE_INC_VERSION}"

UPDATE_MANIFEST_URLS=http://updates.duplicati.com/debug/latest.manifest\;http://alt.updates.duplicati.com/debug/latest.manifest
UPDATE_ZIP_URLS=http://updates.duplicati.com/debug/duplicati.zip\;http://alt.updates.duplicati.com/debug/duplicati.zip
UPDATER_KEYFILE=/Users/kenneth/Dropbox/Privat/Duplicati-updater.key

if [ "x${RELEASE_CHANGEINFO}" == "x" ]; then
    echo "No information in changeinfo file"
    exit 0
fi

echo -n "Enter keyfile password: "
read -s KEYFILE_PASSWORD
echo

echo "${RELEASE_NAME}" > Duplicati/License/VersionTag.txt
echo "${UPDATE_MANIFEST_URLS}" > Duplicati/License/AutoUpdateURL.txt

mono BuildTools/UpdateVersionStamp/bin/Debug/UpdateVersionStamp.exe --version="${RELEASE_VERSION}"
xbuild /p:Configuration=Debug Duplicati.sln

rm Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/*.mdb
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/control_dir
rm Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/Duplicati-server.sqlite
rm Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/Duplicati.debug.log
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/updates

UPDATE_SOURCE=Updates/update_source-${RELEASE_TIMESTAMP}
UPDATE_TARGET=Updates/update_target-${RELEASE_TIMESTAMP}

if [ -e "${UPDATE_SOURCE}" ]; then rm -rf "${UPDATE_SOURCE}"; fi
if [ -e "${UPDATE_TARGET}" ]; then rm -rf "${UPDATE_TARGET}"; fi

mkdir "${UPDATE_SOURCE}"
mkdir "${UPDATE_TARGET}"

cp -R Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/* "${UPDATE_SOURCE}"
cp -R Duplicati/Server/webroot "${UPDATE_SOURCE}"

echo
echo "Building signed package ..."

mono BuildTools/AutoUpdateBuilder/bin/Debug/AutoUpdateBuilder.exe --input="${UPDATE_SOURCE}" --output="${UPDATE_TARGET}" --keyfile="${UPDATER_KEYFILE}" --manifest=Updates/debug.manifest --changeinfo="${RELEASE_CHANGEINFO}" --displayname="${RELEASE_NAME}" --remoteurls="${UPDATE_ZIP_URLS}" --version="${RELEASE_VERSION}" --keyfile-password="$KEYFILE_PASSWORD"

echo "${RELEASE_INC_VERSION}" > "Updates/debug_version.txt"

echo
echo "Built DEBUG version: ${RELEASE_VERSION} - ${RELEASE_NAME}"
echo "    in folder: ${UPDATE_TARGET}"
