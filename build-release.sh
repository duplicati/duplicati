RELEASE_TIMESTAMP=`date +%Y-%m-%d`

RELEASE_INC_VERSION=`cat Updates/build_version.txt`
RELEASE_INC_VERSION=$((RELEASE_INC_VERSION+1))

if [ "x$1" == "x" ]; then
	RELEASE_TYPE="canary"
	echo "No release type specified, using ${RELEASE_TYPE}"
else
	RELEASE_TYPE=$1
fi

RELEASE_VERSION="2.0.1.${RELEASE_INC_VERSION}"
RELEASE_NAME="${RELEASE_VERSION}_${RELEASE_TYPE}_${RELEASE_TIMESTAMP}"

RELEASE_CHANGELOG_FILE="changelog.txt"
RELEASE_CHANGELOG_NEWS_FILE="changelog-news.txt"

RELEASE_FILE_NAME="duplicati-${RELEASE_NAME}"

GIT_STASH_NAME="auto-build-${RELEASE_TIMESTAMP}"

UPDATE_ZIP_URLS="https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip;https://alt.updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip"
UPDATE_MANIFEST_URLS="https://updates.duplicati.com/${RELEASE_TYPE}/latest.manifest;https://alt.updates.duplicati.com/${RELEASE_TYPE}/latest.manifest"
UPDATER_KEYFILE="${HOME}/.config/signkeys/Duplicati/updater-release.key"
GPG_KEYFILE="${HOME}/.config/signkeys/Duplicati/updater-gpgkey.key"
AUTHENTICODE_PFXFILE="${HOME}/.config/signkeys/Duplicati/authenticode.pfx"
AUTHENTICODE_PASSWORD="${HOME}/.config/signkeys/Duplicati/authenticode.key"

GITHUB_TOKEN_FILE="${HOME}/.config/github-api-token"
XBUILD=/Library/Frameworks/Mono.framework/Commands/msbuild
NUGET=/Library/Frameworks/Mono.framework/Commands/nuget
MONO=/Library/Frameworks/Mono.framework/Commands/mono
GPG=/usr/local/bin/gpg2

# Newer GPG needs this to allow input from a non-terminal
export GPG_TTY=$(tty)

if [ ! -f "$GPG" ]; then
	echo "gpg executable not found: $GPG"
	exit 1
fi

if [ ! -f "$XBUILD" ]; then
	echo "xbuild/msbuild executable not found: $XBUILD"
	exit 1
fi

if [ ! -f "$MONO" ]; then
	echo "mono executable not found: $MONO"
	exit 1
fi

if [ ! -f "$NUGET" ]; then
	echo "NuGet executable not found: $NUGET"
	exit 1
fi

# The "OTHER_UPLOADS" setting is no longer used
if [ "${RELEASE_TYPE}" == "nightly" ]; then
	OTHER_UPLOADS=""
elif [ "${RELEASE_TYPE}" == "canary" ]; then
	OTHER_UPLOADS="nightly"
elif [ "${RELEASE_TYPE}" == "experimental" ]; then
	OTHER_UPLOADS="nightly canary"
elif [ "${RELEASE_TYPE}" == "beta" ]; then
	OTHER_UPLOADS="experimental canary nightly"
elif [ "${RELEASE_TYPE}" == "stable" ]; then
	OTHER_UPLOADS="beta experimental canary nightly"
else
	echo "Unsupported release type: ${RELEASE_TYPE}, supported types are: nightly, canary, experimental, beta, stable"
	exit 0
fi


if [ ! -f "${RELEASE_CHANGELOG_FILE}" ]; then
	echo "Changelog file is missing..."
	exit 0
fi

if [ ! -f "${RELEASE_CHANGELOG_NEWS_FILE}" ]; then
	echo "No updates to changelog file found"
	echo
	echo "To make a build without changelog news, run:"
	echo "    touch ""${RELEASE_CHANGELOG_NEWS_FILE}"" "
	exit 0
fi

echo -n "Enter keyfile password: "
read -s KEYFILE_PASSWORD
echo

if [ "z${KEYFILE_PASSWORD}" == "z" ]; then
	echo "No password entered, quitting"
	exit 0
fi

RELEASE_CHANGEINFO_NEWS=`cat "${RELEASE_CHANGELOG_NEWS_FILE}"`

git stash save "${GIT_STASH_NAME}"

if [ ! "x${RELEASE_CHANGEINFO_NEWS}" == "x" ]; then

	echo "${RELEASE_TIMESTAMP} - ${RELEASE_NAME}" > "tmp_changelog.txt"
	echo "==========" >> "tmp_changelog.txt"
	echo "${RELEASE_CHANGEINFO_NEWS}" >> "tmp_changelog.txt"
	echo >> "tmp_changelog.txt"
	cat "${RELEASE_CHANGELOG_FILE}" >> "tmp_changelog.txt"
	cp "tmp_changelog.txt" "${RELEASE_CHANGELOG_FILE}"
	rm "tmp_changelog.txt"
fi

echo "${RELEASE_NAME}" > "Duplicati/License/VersionTag.txt"
echo "${RELEASE_TYPE}" > "Duplicati/Library/AutoUpdater/AutoUpdateBuildChannel.txt"
echo "${UPDATE_MANIFEST_URLS}" > "Duplicati/Library/AutoUpdater/AutoUpdateURL.txt"
cp "Updates/release_key.txt"  "Duplicati/Library/AutoUpdater/AutoUpdateSignKey.txt"

RELEASE_CHANGEINFO=`cat ${RELEASE_CHANGELOG_FILE}`
if [ "x${RELEASE_CHANGEINFO}" == "x" ]; then
    echo "No information in changeinfo file"
    exit 0
fi

rm -rf "Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release"

"${XBUILD}" /property:Configuration=Release "BuildTools/UpdateVersionStamp/UpdateVersionStamp.csproj"
"${MONO}" "BuildTools/UpdateVersionStamp/bin/Release/UpdateVersionStamp.exe" --version="${RELEASE_VERSION}"

"${NUGET}" restore "BuildTools/AutoUpdateBuilder/AutoUpdateBuilder.sln"
"${NUGET}" restore "Duplicati.sln"

"${XBUILD}" /p:Configuration=Debug "BuildTools/AutoUpdateBuilder/AutoUpdateBuilder.sln"

"${XBUILD}" /p:Configuration=Release /target:Clean "Duplicati.sln"
find "Duplicati" -type d -name "Release" | xargs rm -rf
"${XBUILD}" /p:DefineConstants=__MonoCS__ /p:DefineConstants=ENABLE_GTK /p:Configuration=Release "Duplicati.sln"
BUILD_STATUS=$?

if [ "${BUILD_STATUS}" -ne 0 ]; then
    echo "Failed to build, xbuild gave ${BUILD_STATUS}, exiting"
    exit 4
fi

if [ ! -d "Updates/build" ]; then mkdir "Updates/build"; fi

UPDATE_SOURCE=Updates/build/${RELEASE_TYPE}_source-${RELEASE_VERSION}
UPDATE_TARGET=Updates/build/${RELEASE_TYPE}_target-${RELEASE_VERSION}

if [ -e "${UPDATE_SOURCE}" ]; then rm -rf "${UPDATE_SOURCE}"; fi
if [ -e "${UPDATE_TARGET}" ]; then rm -rf "${UPDATE_TARGET}"; fi

mkdir "${UPDATE_SOURCE}"
mkdir "${UPDATE_TARGET}"

cp -R Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/* "${UPDATE_SOURCE}"
cp -R Duplicati/Server/webroot "${UPDATE_SOURCE}"

# We copy some files for alphavss manually as they are not picked up by xbuild
mkdir "${UPDATE_SOURCE}/alphavss"
for FN in Duplicati/Library/Snapshots/bin/Release/AlphaVSS.*.dll; do
	cp "${FN}" "${UPDATE_SOURCE}/alphavss/"
done

# Install the assembly redirects for all Duplicati .exe files
find "${UPDATE_SOURCE}" -type f -name Duplicati.*.exe -maxdepth 1 -exec cp Installer/AssemblyRedirects.xml {}.config \;

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

# Sign all files with Authenticode
if [ -f "${AUTHENTICODE_PFXFILE}" ] && [ -f "${AUTHENTICODE_PASSWORD}" ]; then
	echo "Performing authenticode signing of executables and libraries"

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

	for exec in "${UPDATE_SOURCE}/Duplicati."*.exe; do
		authenticode_sign "${exec}"
	done
	for exec in "${UPDATE_SOURCE}/Duplicati."*.dll; do
		authenticode_sign "${exec}"
	done

else
	echo "Skipped authenticode signing as files are missing"
fi

echo
echo "Building signed package ..."

"${MONO}" "BuildTools/AutoUpdateBuilder/bin/Debug/AutoUpdateBuilder.exe" --input="${UPDATE_SOURCE}" --output="${UPDATE_TARGET}" --keyfile="${UPDATER_KEYFILE}" --manifest=Updates/${RELEASE_TYPE}.manifest --changeinfo="${RELEASE_CHANGEINFO}" --displayname="${RELEASE_NAME}" --remoteurls="${UPDATE_ZIP_URLS}" --version="${RELEASE_VERSION}" --keyfile-password="${KEYFILE_PASSWORD}" --gpgkeyfile="${GPG_KEYFILE}" --gpgpath="${GPG}"

if [ ! -f "${UPDATE_TARGET}/package.zip" ]; then
	"${MONO}" "BuildTools/UpdateVersionStamp/bin/Debug/UpdateVersionStamp.exe" --version="2.0.0.7"	
	
	echo "Something went wrong while building the package, no output found"
	exit 5
fi

echo "${RELEASE_INC_VERSION}" > "Updates/build_version.txt"

mv "${UPDATE_TARGET}/package.zip" "${UPDATE_TARGET}/latest.zip"
mv "${UPDATE_TARGET}/autoupdate.manifest" "${UPDATE_TARGET}/latest.manifest"
mv "${UPDATE_TARGET}/package.zip.sig" "${UPDATE_TARGET}/latest.zip.sig"
mv "${UPDATE_TARGET}/package.zip.sig.asc" "${UPDATE_TARGET}/latest.zip.sig.asc"
cp "${UPDATE_TARGET}/latest.zip" "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip"
cp "${UPDATE_TARGET}/latest.manifest" "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.manifest"
cp "${UPDATE_TARGET}/latest.zip.sig" "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip.sig"
cp "${UPDATE_TARGET}/latest.zip.sig.asc" "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip.sig.asc"

"${MONO}" "BuildTools/UpdateVersionStamp/bin/Debug/UpdateVersionStamp.exe" --version="2.0.0.7"

echo "Uploading binaries"
aws --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip" "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip"
aws --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip.sig" "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig"
aws --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip.sig.asc" "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig.asc"
aws --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.manifest" "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.manifest"

aws --profile=duplicati-upload s3 cp "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.manifest" "s3://updates.duplicati.com/${RELEASE_TYPE}/latest.manifest"

ZIP_MD5=`md5 ${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip | awk -F ' ' '{print $NF}'`
ZIP_SHA1=`shasum -a 1 ${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip | awk -F ' ' '{print $1}'`
ZIP_SHA256=`shasum -a 256 ${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip | awk -F ' ' '{print $1}'`

cat > "latest.json" <<EOF
{
	"version": "${RELEASE_VERSION}",
	"zip": "${RELEASE_FILE_NAME}.zip",
	"zipsig": "${RELEASE_FILE_NAME}.zip.sig",
	"zipsigasc": "${RELEASE_FILE_NAME}.zip.sig.asc",
	"manifest": "${RELEASE_FILE_NAME}.manifest",
	"urlbase": "https://updates.duplicati.com/${RELEASE_TYPE}/",
	"link": "https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip",
	"zipmd5": "${ZIP_MD5}",
	"zipsha1": "${ZIP_SHA1}",
	"zipsha256": "${ZIP_SHA256}"
}
EOF

echo "duplicati_version_info =" > "latest.js"
cat "latest.json" >> "latest.js"
echo ";" >> "latest.js"

aws --profile=duplicati-upload s3 cp "latest.json" "s3://updates.duplicati.com/${RELEASE_TYPE}/latest.json"
aws --profile=duplicati-upload s3 cp "latest.js" "s3://updates.duplicati.com/${RELEASE_TYPE}/latest.js"

# echo "Propagating to other build types"
# for OTHER in ${OTHER_UPLOADS}; do
# 	aws --profile=duplicati-upload s3 cp "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.manifest" "s3://updates.duplicati.com/${OTHER}/latest.manifest"
# 	aws --profile=duplicati-upload s3 cp "s3://updates.duplicati.com/${RELEASE_TYPE}/latest.json" "s3://updates.duplicati.com/${OTHER}/latest.json"
# done

rm "${RELEASE_CHANGELOG_NEWS_FILE}"

git checkout "Duplicati/License/VersionTag.txt"
git checkout "Duplicati/Library/AutoUpdater/AutoUpdateURL.txt"
git checkout "Duplicati/Library/AutoUpdater/AutoUpdateBuildChannel.txt"
git add "Updates/build_version.txt"
git add "${RELEASE_CHANGELOG_FILE}"
git commit -m "Version bump to v${RELEASE_VERSION}-${RELEASE_NAME}" -m "You can download this build from: " -m "Binaries: https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip" -m "Signature file: https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig" -m "ASCII signature file: https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig.asc" -m "MD5: ${ZIP_MD5}" -m "SHA1: ${ZIP_SHA1}" -m "SHA256: ${ZIP_SHA256}"
git tag "v${RELEASE_VERSION}-${RELEASE_NAME}"                       -m "You can download this build from: " -m "Binaries: https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip" -m "Signature file: https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig" -m "ASCII signature file: https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig.asc" -m "MD5: ${ZIP_MD5}" -m "SHA1: ${ZIP_SHA1}" -m "SHA256: ${ZIP_SHA256}"
git push --tags

PRE_RELEASE_LABEL="--pre-release"
if [ "${RELEASE_TYPE}" == "stable" ]; then
	PRE_RELEASE_LABEL=""
fi

RELEASE_MESSAGE=`printf "Changes in this version:\n${RELEASE_CHANGEINFO_NEWS}"`

# Using the tool from https://github.com/aktau/github-release

GITHUB_TOKEN=`cat "${GITHUB_TOKEN_FILE}"`

if [ "x${GITHUB_TOKEN}" == "x" ]; then
	echo "No GITHUB_TOKEN found in environment, you can manually upload the binaries"
else
	github-release release ${PRE_RELEASE_LABEL} \
	    --tag "v${RELEASE_VERSION}-${RELEASE_NAME}"  \
	    --name "v${RELEASE_VERSION}-${RELEASE_NAME}" \
	    --repo "duplicati" \
	    --user "duplicati" \
	    --security-token "${GITHUB_TOKEN}" \
	    --description "${RELEASE_MESSAGE}" \

	github-release upload \
	    --tag "v${RELEASE_VERSION}-${RELEASE_NAME}"  \
	    --name "${RELEASE_FILE_NAME}.zip" \
	    --repo "duplicati" \
	    --user "duplicati" \
	    --security-token "${GITHUB_TOKEN}" \
	    --file "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip"
fi

echo
echo "Built ${RELEASE_TYPE} version: ${RELEASE_VERSION} - ${RELEASE_NAME}"
echo "    in folder: ${UPDATE_TARGET}"
echo
echo
echo "Building installers ..."

# Send the password along to avoid typing it again
export KEYFILE_PASSWORD

bash "build-installers.sh" "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip"


