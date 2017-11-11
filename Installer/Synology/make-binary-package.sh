#!/bin/bash

if [ ! -f "$1" ]; then
	echo "Please provide the filename of an existing zip build as the first argument"
	exit
fi

FILENAME=`basename $1`
DIRNAME=`echo "${FILENAME}" | cut -d "_" -f 1`
VERSION=`echo "${DIRNAME}" | cut -d "-" -f 2`
DATE_STAMP=`LANG=C date -R`
BASE_FILE_NAME="${FILENAME%.*}"
TMPDIRNAME="${BASE_FILE_NAME}-extract"
MONO=/Library/Frameworks/Mono.framework/Commands/mono
GPG_KEYFILE="${HOME}/.config/signkeys/Duplicati/updater-gpgkey.key"

# Sort on macOS does not have -V / --version-sort
# https://stackoverflow.com/questions/4493205/unix-sort-of-version-numbers
SORT_OPTIONS="-t. -k 1,1n -k 2,2n -k 3,3n -k 4,4n"

if [ -d "${DIRNAME}" ]; then
	rm -rf "${DIRNAME}"
fi

if [ -d "${TMPDIRNAME}" ]; then
    rm -rf "${TMPDIRNAME}"
fi

if [ -f "package.tgz" ]; then
    rm -rf "package.tgz"
fi

if [ -f "${BASE_FILE_NAME}.spk" ]; then
    rm -rf "${BASE_FILE_NAME}.spk"
fi

if [ -f "${BASE_FILE_NAME}.spk.tmp" ]; then
    rm -rf "${BASE_FILE_NAME}.spk.tmp"
fi

if [ -f "${BASE_FILE_NAME}.signature" ]; then
    rm -rf "${BASE_FILE_NAME}.spk.signature"
fi

TIMESERVER="http://timestamp.synology.com/timestamp.php"

unzip -q -d "${DIRNAME}" "$1"

for n in "../oem" "../../oem" "../../../oem"
do
    if [ -d $n ]; then
        echo "Installing OEM files"
        cp -R $n "${DIRNAME}/webroot/"
    fi
done

for n in "oem-app-name.txt" "oem-update-url.txt" "oem-update-key.txt" "oem-update-readme.txt" "oem-update-installid.txt"
do
    for p in "../$n" "../../$n" "../../../$n"
    do
        if [ -f $p ]; then
            echo "Installing OEM override file"
            cp $p "${DIRNAME}"
        fi
    done
done

cd "${DIRNAME}"

# Remove items unused on the Synology platform
rm -rf ./win-tools
rm -rf ./SQLite/win64
rm -rf ./SQLite/win32
rm -rf ./MonoMac.dll
rm -rf ./alphavss
rm -rf ./OSX\ Icons
rm -rf ./OSXTrayHost
rm ./AlphaFS.dll
rm ./AlphaVSS.Common.dll
rm -rf ./licenses/alphavss
rm -rf ./licenses/MonoMac
rm -rf ./licenses/gpg

# Install extra items for Synology
cp -R ../web-extra/* webroot/
cp ../dsm.duplicati.conf .

DIRSIZE_KB=`BLOCKSIZE=1024 du -s | cut -d '.' -f 1`
let "DIRSIZE=DIRSIZE_KB*1024"

tar cf ../package.tgz ./*
cd ..

rm -rf "${DIRNAME}"

ICON_72=$(openssl base64 -A -in PACKAGE_ICON.PNG)
ICON_256=$(openssl base64 -A -in PACKAGE_ICON_256.PNG)

git checkout INFO
echo "version=\"${VERSION}\"" >> "INFO"
MD5=`md5 "package.tgz" | awk -F ' ' '{print $NF}'` 
echo "checksum=\"${MD5}\"" >> "INFO"
echo "extractsize=\"${DIRSIZE}\"" >> "INFO"
echo "package_icon=\"${ICON_72}\"" >> "INFO"
echo "package_icon_256=\"${ICON_256}\"" >> "INFO"

chmod +x scripts/*

tar cf "${BASE_FILE_NAME}.spk" INFO LICENSE *.PNG package.tgz scripts conf WIZARD_UIFILES

git checkout INFO
rm package.tgz

if [ -f "${GPG_KEYFILE}" ]; then
    if [ "z${KEYFILE_PASSWORD}" == "z" ]; then
        echo -n "Enter keyfile password: "
        read -s KEYFILE_PASSWORD
        echo
    fi

    GPGDATA=`"${MONO}" "../../BuildTools/AutoUpdateBuilder/bin/Debug/SharpAESCrypt.exe" d "${KEYFILE_PASSWORD}" "${GPG_KEYFILE}"`
    if [ ! $? -eq 0 ]; then
        echo "Decrypting GPG keyfile failed"
        exit 1
    fi
    GPGID=`echo "${GPGDATA}" | head -n 1`
    GPGKEY=`echo "${GPGDATA}" | head -n 2 | tail -n 1`
else
    echo "No GPG keyfile found, skipping gpg signing"
fi

if [ "z${GPGID}" != "z" ]; then
    # Now codesign the spk file
    mkdir "${TMPDIRNAME}"
    tar xf "${BASE_FILE_NAME}.spk" -C "${TMPDIRNAME}"
    cat $(find ${TMPDIRNAME} -type f | sort ${SORT_OPTIONS}) > "${BASE_FILE_NAME}.spk.tmp"

    gpg2 --ignore-time-conflict --ignore-valid-from --yes --batch --armor --detach-sign --default-key="${GPGID}" --output "${BASE_FILE_NAME}.signature" "${BASE_FILE_NAME}.spk.tmp"
    rm "${BASE_FILE_NAME}.spk.tmp"

    curl --silent --form "file=@${BASE_FILE_NAME}.signature" "${TIMESERVER}" > "${TMPDIRNAME}/syno_signature.asc"
    rm "${BASE_FILE_NAME}.signature"

    rm "${BASE_FILE_NAME}.spk"
    tar cf "${BASE_FILE_NAME}.spk" -C "${TMPDIRNAME}" `ls -1 ${TMPDIRNAME}`

    rm -rf "${TMPDIRNAME}"
fi