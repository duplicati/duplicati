RELEASE_TIMESTAMP=`date +%Y-%m-%d`

RELEASE_INC_VERSION=`cat Updates/debug_version.txt`
RELEASE_INC_VERSION=$((RELEASE_INC_VERSION+1))

RELEASE_NAME=2.0_CLI_experimental_${RELEASE_TIMESTAMP}
RELEASE_CHANGEINFO=`cat Updates/debug_changeinfo.txt`
RELEASE_VERSION="2.0.0.${RELEASE_INC_VERSION}"

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
cp "Updates/debug_urls.txt"  Duplicati/Library/AutoUpdater/AutoUpdateURL.txt
cp "Updates/debug_key.txt"  Duplicati/Library/AutoUpdater/AutoUpdateSignKey.txt

mono BuildTools/UpdateVersionStamp/bin/Debug/UpdateVersionStamp.exe --version="${RELEASE_VERSION}"
xbuild /p:Configuration=Debug BuildTools/AutoUpdateBuilder/AutoUpdateBuilder.sln
xbuild /p:Configuration=Debug Duplicati.sln

if [ ! -d "Updates/build" ]; then mkdir "Updates/build"; fi

UPDATE_SOURCE=Updates/build/update_source-${RELEASE_TIMESTAMP}
UPDATE_TARGET=Updates/build/update_target-${RELEASE_TIMESTAMP}

if [ -e "${UPDATE_SOURCE}" ]; then rm -rf "${UPDATE_SOURCE}"; fi
if [ -e "${UPDATE_TARGET}" ]; then rm -rf "${UPDATE_TARGET}"; fi

mkdir "${UPDATE_SOURCE}"
mkdir "${UPDATE_TARGET}"

# TODO: Figure out how to deal with OSX updates
#cp -R Duplicati/GUI/Duplicati.GUI.MacTrayIcon/bin/Debug/* "${UPDATE_SOURCE}"
cp -R Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Debug/* "${UPDATE_SOURCE}"
cp -R Duplicati/Server/webroot "${UPDATE_SOURCE}"

rm ${UPDATE_SOURCE}/*.mdb
if [ -e "${UPDATE_SOURCE}/control_dir" ]; then rm -rf "${UPDATE_SOURCE}/control_dir"; fi
if [ -e "${UPDATE_SOURCE}/Duplicati-server.sqlite" ]; then rm "${UPDATE_SOURCE}/Duplicati-server.sqlite"; fi
if [ -e "${UPDATE_SOURCE}/Duplicati.debug.log" ]; then rm "${UPDATE_SOURCE}/Duplicati.debug.log"; fi
if [ -e "${UPDATE_SOURCE}/updates" ]; then rm -rf "${UPDATE_SOURCE}/updates"; fi

echo
echo "Building signed package ..."

mono BuildTools/AutoUpdateBuilder/bin/Debug/AutoUpdateBuilder.exe --input="${UPDATE_SOURCE}" --output="${UPDATE_TARGET}" --keyfile="${UPDATER_KEYFILE}" --manifest=Updates/debug.manifest --changeinfo="${RELEASE_CHANGEINFO}" --displayname="${RELEASE_NAME}" --remoteurls="${UPDATE_ZIP_URLS}" --version="${RELEASE_VERSION}" --keyfile-password="$KEYFILE_PASSWORD"

echo "${RELEASE_INC_VERSION}" > "Updates/debug_version.txt"

mv "${UPDATE_TARGET}/package.zip" "${UPDATE_TARGET}/duplicati.zip"
mv "${UPDATE_TARGET}/autoupdate.manifest" "${UPDATE_TARGET}/latest.manifest"

mono BuildTools/UpdateVersionStamp/bin/Debug/UpdateVersionStamp.exe --version="2.0.0.7"

echo
echo "Built DEBUG version: ${RELEASE_VERSION} - ${RELEASE_NAME}"
echo "    in folder: ${UPDATE_TARGET}"


