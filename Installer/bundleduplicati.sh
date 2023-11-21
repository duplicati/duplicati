#!/bin/bash
# ripped from build-release.sh

# directory where files are stored
UPDATE_SOURCE="${RUNTMP}/tmpinstduplicati"
# zip output to be used by the installers
ZIPRESULT="${RUNTMP}/$1" 

if [ -e "${UPDATE_SOURCE}" ]; then rm -rf "${UPDATE_SOURCE}"; fi
if [ -f "${ZIPRESULT}" ]; then rm -rf "${ZIPRESULT}"; fi

mkdir -p "${UPDATE_SOURCE}"

cp -R Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/* "${UPDATE_SOURCE}"
cp -R Duplicati/Server/webroot "${UPDATE_SOURCE}"

# We copy some files for alphavss manually as they are not picked up by xbuild
mkdir "${UPDATE_SOURCE}/alphavss"
for FN in Duplicati/Library/Snapshots/bin/Release/AlphaVSS.*.dll; do
	cp "${FN}" "${UPDATE_SOURCE}/alphavss/"
done

# Fix for some support libraries not being picked up
for BACKEND in Duplicati/Library/Backend/*; do
	if [ -d "${BACKEND}/bin/Release/" ]; then
		cp "${BACKEND}/bin/Release/"*.dll "${UPDATE_SOURCE}"
	fi
done

# Install the assembly redirects for all Duplicati .exe files
find "${UPDATE_SOURCE}" -maxdepth 1 -type f -name Duplicati.*.exe -exec cp Installer/AssemblyRedirects.xml {}.config \;

# Clean some unwanted build files
for FILE in "control_dir" "Duplicati-server.sqlite" "Duplicati.debug.log" "updates"; do
	if [ -e "${UPDATE_SOURCE}/${FILE}" ]; then rm -rf "${UPDATE_SOURCE}/${FILE}"; fi
done

# Clean the localization spam from Azure
for FILE in "de" "es" "fr" "it" "ja" "ko" "ru" "zh-Hans" "zh-Hant"; do
	if [ -e "${UPDATE_SOURCE}/${FILE}" ]; then rm -rf "${UPDATE_SOURCE}/${FILE}"; fi
done

# Clean debug files, if any
rm -rf "${UPDATE_SOURCE}/"*.mdb;
rm -rf "${UPDATE_SOURCE}/"*.pdb;

# Remove all library docs files
rm -rf "${UPDATE_SOURCE}/"*.xml;

# Remove all .DS_Store and Thumbs.db files
find  . -type f -name ".DS_Store" | xargs rm -rf
find  . -type f -name "Thumbs.db" | xargs rm -rf

bash Installer/test.sh "${UPDATE_SOURCE}"
# bundle everything info a zip file
pushd "${UPDATE_SOURCE}"
rm "Build*" -rf
mv * $RUNTMP/artifacts/
popd
rm "${UPDATE_SOURCE}" -rf
