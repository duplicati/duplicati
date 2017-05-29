#!/bin/bash

if [ ! -f "$1" ]
then
  echo "Please supply the path to an existing zip binary as the first argument"
  exit 1
fi

while true
do
  DOCKER_RESULT=$(docker ps)
  if [ "$?" != "0" ]
  then
    echo "It appears the Docker daemon is not running, make sure you started it"
    read -p "Press [Enter] key to AFTER you started Docker"
    continue
  fi
  break
done

GITHUB_TOKEN_FILE="${HOME}/.config/github-api-token"
GPG_KEYFILE="${HOME}/.config/signkeys/Duplicati/updater-gpgkey.key"
AUTHENTICODE_PFXFILE="${HOME}/.config/signkeys/Duplicati/authenticode.pfx"
AUTHENTICODE_PASSWORD="${HOME}/.config/signkeys/Duplicati/authenticode.key"
MONO=/Library/Frameworks/Mono.framework/Commands/mono

GPG=/usr/local/bin/gpg2

ZIPFILE=`basename "$1"`
VERSION=`echo "${ZIPFILE}" | cut -d "-" -f 2 | cut -d "_" -f 1`
BUILDTYPE=`echo "${ZIPFILE}" | cut -d "-" -f 2 | cut -d "_" -f 2`
BUILDTAG_RAW=`echo "${ZIPFILE}" | cut -d "." -f 1-4 | cut -d "-" -f 2-4`
BUILDTAG="${BUILDTAG_RAW//-}"

RPMNAME="duplicati-${VERSION}-${BUILDTAG}.noarch.rpm"
DEBNAME="duplicati_${VERSION}-1_all.deb"
MSI64NAME="duplicati-${BUILDTAG_RAW}-x64.msi"
MSI32NAME="duplicati-${BUILDTAG_RAW}-x86.msi"
DMGNAME="duplicati-${BUILDTAG_RAW}.dmg"
PKGNAME="duplicati-${BUILDTAG_RAW}.pkg"
SPKNAME="duplicati-${BUILDTAG_RAW}.spk"
SIGNAME="duplicati-${BUILDTAG_RAW}-signatures.zip"

UPDATE_TARGET="Updates/build/${BUILDTYPE}_target-${VERSION}"

echo "Filename: ${ZIPFILE}"
echo "Version: ${VERSION}"
echo "Buildtype: ${BUILDTYPE}"
echo "Buildtag: ${BUILDTAG}"
echo "RPMName: ${RPMNAME}"
echo "DEBName: ${DEBNAME}"
echo "SPKName: ${SPKNAME}"

build_file_signatures() {
	if [ "z${GPGID}" != "z" ]; then
		echo "$GPGKEY" | "${GPG}" "--passphrase-fd" "0" "--batch" "--yes" "--default-key=${GPGID}" "--output" "$2.sig" "--detach-sig" "$1"
		echo "$GPGKEY" | "${GPG}" "--passphrase-fd" "0" "--batch" "--yes" "--default-key=${GPGID}" "--armor" "--output" "$2.sig.asc" "--detach-sig" "$1"
	fi

	md5 "$1" | awk -F ' ' '{print $NF}' > "$2.md5"
	shasum -a 1 "$1" | awk -F ' ' '{print $1}' > "$2.sha1"
	shasum -a 256 "$1" | awk -F ' ' '{print $1}'  > "$2.sha256"
}

if [ -f "${GPG_KEYFILE}" ]; then
	if [ "z${KEYFILE_PASSWORD}" == "z" ]; then
		echo -n "Enter keyfile password: "
		read -s KEYFILE_PASSWORD
		echo
	fi

	GPGDATA=`"${MONO}" "BuildTools/AutoUpdateBuilder/bin/Debug/SharpAESCrypt.exe" d "${KEYFILE_PASSWORD}" "${GPG_KEYFILE}"`
	if [ ! $? -eq 0 ]; then
		echo "Decrypting GPG keyfile failed"
		exit 1
	fi
	GPGID=`echo "${GPGDATA}" | head -n 1`
	GPGKEY=`echo "${GPGDATA}" | head -n 2 | tail -n 1`
else
	echo "No GPG keyfile found, skipping gpg signatures"
fi

# Pre-boot virtual machine
echo "Booting Win10 build instance"
VBoxHeadless --startvm Duplicati-Win10-Build &


# Then do the local build to mask the waiting a little more

echo ""
echo ""
echo "Building OSX package locally ..."
echo ""
echo "Enter local sudo password..."

cd "Installer/OSX"
bash "make-dmg.sh" "../../$1"
mv "Duplicati.dmg" "../../${UPDATE_TARGET}/${DMGNAME}"
mv "Duplicati.pkg" "../../${UPDATE_TARGET}/${PKGNAME}"
cd "../.."

echo ""
echo ""
echo "Building Synology package locally ..."

cd Installer/Synology
bash "make-binary-package.sh" "../../$1"
mv "${SPKNAME}" "../../${UPDATE_TARGET}/"
cd ../..


echo ""
echo ""
echo "Building Debian deb with Docker ..."

cd "Installer/debian"
bash "docker-build-binary.sh" "../../$1"
cd "../.."

mv "Installer/debian/${DEBNAME}" "${UPDATE_TARGET}"

echo "Done building deb package"



echo ""
echo ""
echo "Building Fedora RPM with Docker ..."

cd "Installer/fedora"
bash "docker-build-binary.sh" "../../$1"
cd "../.."

mv "Installer/fedora/${RPMNAME}" "${UPDATE_TARGET}"

echo "Done building rpm package"


echo ""
echo ""
echo "Building Windows instance in virtual machine"

while true
do
    ssh -o ConnectTimeout=5 IEUser@192.168.56.101 "dir"
    if [ $? -eq 255 ]; then
    	echo "Windows Build machine is not responding, try restarting it"
        read -p "Press [Enter] key to try again"
        continue
    fi
    break
done

cat > "tmp-windows-commands.bat" <<EOF
SET VS120COMNTOOLS=%VS140COMNTOOLS%
cd \\Duplicati\\Installer\\Windows
build-msi.bat "../../$1"
EOF

ssh IEUser@192.168.56.101 "\\Duplicati\\tmp-windows-commands.bat"
ssh IEUser@192.168.56.101 "shutdown /s /t 0"

rm "tmp-windows-commands.bat"

mv "./Installer/Windows/Duplicati.msi" "${UPDATE_TARGET}/${MSI64NAME}"
mv "./Installer/Windows/Duplicati-32bit.msi" "${UPDATE_TARGET}/${MSI32NAME}"

if [ -f "${AUTHENTICODE_PFXFILE}" ] && [ -f "${AUTHENTICODE_PASSWORD}" ]; then
	echo "Performing authenticode signing of installers"

	if [ "z${KEYFILE_PASSWORD}" == "z" ]; then
		echo -n "Enter keyfile password: "
		read -s KEYFILE_PASSWORD
		echo
	fi

	authenticode_sign() {
		NEST=""
		for hashalg in sha1 sha256; do
			SIGN_MSG=`osslsigncode sign -pkcs12 "${AUTHENTICODE_PFXFILE}" -pass "${PFX_PASS}" -n "Duplicati" -i "http://www.duplicati.com" -h "${hashalg}" ${NEST} -t "http://timestamp.verisign.com/scripts/timstamp.dll" -in "$1" -out tmpfile`
			if [ "${SIGN_MSG}" != "Succeeded" ]; then echo "${SIGN_MSG}"; fi
			mv tmpfile "$1"
			NEST="-nest"
		done
	}

	PFX_PASS=`"${MONO}" "BuildTools/AutoUpdateBuilder/bin/Debug/SharpAESCrypt.exe" d "${KEYFILE_PASSWORD}" "${AUTHENTICODE_PASSWORD}"`

	DECRYPT_STATUS=$?
	if [ "${DECRYPT_STATUS}" -ne 0 ]; then
	    echo "Failed to decrypt, SharpAESCrypt gave status ${DECRYPT_STATUS}, exiting"
	    exit 4
	fi

	if [ "x${PFX_PASS}" == "x" ]; then
	    echo "Failed to decrypt, SharpAESCrypt gave empty password, exiting"
	    exit 4
	fi

	authenticode_sign "${UPDATE_TARGET}/${MSI64NAME}"
	authenticode_sign "${UPDATE_TARGET}/${MSI32NAME}"

else
	echo "Skipped authenticode signing as files are missing"
fi

echo ""
echo ""
echo "Done building, uploading installers ..."

if [ -d "./tmp" ]; then
	rm -rf "./tmp"
fi

mkdir "./tmp"

echo "{" > "./tmp/latest-installers.json"

process_installer() {
	if [ "$2" != "zip" ]; then
		aws --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/$1" "s3://updates.duplicati.com/${BUILDTYPE}/$1"
	fi

	local MD5=`md5 ${UPDATE_TARGET}/$1 | awk -F ' ' '{print $NF}'`
	local SHA1=`shasum -a 1 ${UPDATE_TARGET}/$1 | awk -F ' ' '{print $1}'`
	local SHA256=`shasum -a 256 ${UPDATE_TARGET}/$1 | awk -F ' ' '{print $1}'`

cat >> "./tmp/latest-installers.json" <<EOF
	"$2": {
		"name": "$1",
		"url": "https://updates.duplicati.com/${BUILDTYPE}/$1",
		"md5": "${MD5}",
		"sha1": "${SHA1}",
		"sha256": "${SHA256}"
	},
EOF

}

process_installer "${ZIPFILE}" "zip"
process_installer "${SPKNAME}" "spk"
process_installer "${RPMNAME}" "rpm"
process_installer "${DEBNAME}" "deb"
process_installer "${DMGNAME}" "dmg"
process_installer "${PKGNAME}" "pkg"
process_installer "${MSI32NAME}" "msi86"
process_installer "${MSI64NAME}" "msi64"

cat >> "./tmp/latest-installers.json" <<EOF
	"version": "${VERSION}"
}
EOF

echo "duplicati_installers =" > "./tmp/latest-installers.js"
cat "./tmp/latest-installers.json" >> "./tmp/latest-installers.js"
echo ";" >> "./tmp/latest-installers.js"

aws --profile=duplicati-upload s3 cp "./tmp/latest-installers.json" "s3://updates.duplicati.com/${BUILDTYPE}/latest-installers.json"
aws --profile=duplicati-upload s3 cp "./tmp/latest-installers.js" "s3://updates.duplicati.com/${BUILDTYPE}/latest-installers.js"

if [ -d "./tmp" ]; then
	rm -rf "./tmp"
fi

SIG_FOLDER="duplicati-${BUILDTAG_RAW}-signatures"
mkdir tmp
mkdir "./tmp/${SIG_FOLDER}"

for FILE in "${SPKNAME}" "${RPMNAME}" "${DEBNAME}" "${DMGNAME}" "${PKGNAME}" "${MSI32NAME}" "${MSI64NAME}" "${ZIPFILE}"; do
	build_file_signatures "${UPDATE_TARGET}/${FILE}" "./tmp/${SIG_FOLDER}/${FILE}"
done

if [ "z${GPGID}" != "z" ]; then
	echo "${GPGID}" > "./tmp/${SIG_FOLDER}/sign-key.txt"
	echo "https://pgp.mit.edu/pks/lookup?op=get&search=${GPGID}" >> "./tmp/${SIG_FOLDER}/sign-key.txt"
fi

if [ -f "${UPDATE_TARGET}/${SIGNAME}" ]; then
	rm "${UPDATE_TARGET}/${SIGNAME}"
fi

cd tmp
zip -r9 "./${SIGNAME}" "./${SIG_FOLDER}/"
cd ..

mv "./tmp/${SIGNAME}" "${UPDATE_TARGET}/${SIGNAME}"
rm -rf "./tmp/${SIG_FOLDER}"

aws --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/${SIGNAME}" "s3://updates.duplicati.com/${BUILDTYPE}/${SIGNAME}"

GITHUB_TOKEN=`cat "${GITHUB_TOKEN_FILE}"`

if [ "x${GITHUB_TOKEN}" == "x" ]; then
	echo "No GITHUB_TOKEN found in environment, you can manually upload the binaries"
else
	for FILE in "${SPKNAME}" "${RPMNAME}" "${DEBNAME}" "${DMGNAME}" "${PKGNAME}" "${MSI32NAME}" "${MSI64NAME}" "${SIGNAME}"; do
		github-release upload \
		    --tag "v${VERSION}-${BUILDTAG_RAW}"  \
		    --name "${FILE}" \
		    --repo "duplicati" \
		    --user "duplicati" \
		    --security-token "${GITHUB_TOKEN}" \
		    --file "${UPDATE_TARGET}/${FILE}"
	done
fi

rm -rf "./tmp"

if [ -f ~/.config/duplicati-mirror-sync.sh ]; then
    bash ~/.config/duplicati-mirror-sync.sh
else
    echo "Skipping CDN update"
fi

VBoxManage controlvm "Duplicati-Win10-Build" poweroff

