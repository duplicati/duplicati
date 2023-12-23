RELEASE_TIMESTAMP=$(date +%Y-%m-%d)

RELEASE_INC_VERSION=$(cat Updates/build_version.txt)
RELEASE_INC_VERSION=$((RELEASE_INC_VERSION+1))

if [ "x$1" == "x" ]; then
	RELEASE_TYPE="canary"
	echo "No release type specified, using ${RELEASE_TYPE}"
else
	RELEASE_TYPE=$1
fi

RELEASE_VERSION="2.0.7.${RELEASE_INC_VERSION}"
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
DISCOURSE_TOKEN_FILE="${HOME}/.config/discourse-api-token"
XBUILD=/Library/Frameworks/Mono.framework/Commands/msbuild
NUGET=/Library/Frameworks/Mono.framework/Commands/nuget
MONO=/Library/Frameworks/Mono.framework/Commands/mono
XAMARIN=/Library/Frameworks/Xamarin.Mac.framework
GPG=/usr/local/bin/gpg2
AWS=/usr/local/bin/aws
GITHUB_RELEASE=/usr/local/bin/github-release

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

if [ ! -f "$AWS" ]; then
	echo "aws-cli not found: $AWS"
	exit 1
fi

if [ ! -f "$GITHUB_RELEASE" ]; then
	echo "github-release executable not found: $GITHUB_RELEASE"
	echo "Grab it from: https://github.com/aktau/github-release"
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

echo "Activating sudo rights for building the installers later, please enter sudo password:"
sudo echo "Sudo activated"

RELEASE_CHANGEINFO_NEWS=$(cat "${RELEASE_CHANGELOG_NEWS_FILE}")

git stash save "${GIT_STASH_NAME}"
git pull

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

RELEASE_CHANGEINFO=$(cat ${RELEASE_CHANGELOG_FILE})
if [ "x${RELEASE_CHANGEINFO}" == "x" ]; then
    echo "No information in changeinfo file"
    exit 0
fi

rm -rf "Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release"

"${NUGET}" restore "BuildTools/UpdateVersionStamp/UpdateVersionStamp.sln"
"${XBUILD}" /property:Configuration=Release "BuildTools/UpdateVersionStamp/UpdateVersionStamp.sln"
"${MONO}" "BuildTools/UpdateVersionStamp/bin/Release/UpdateVersionStamp.exe" --version="${RELEASE_VERSION}"

"${NUGET}" restore "BuildTools/AutoUpdateBuilder/AutoUpdateBuilder.sln"
"${NUGET}" restore "BuildTools/GnupgSigningTool/GnupgSigningTool.sln"
"${NUGET}" restore "Duplicati.sln"

"${XBUILD}" /p:Configuration=Debug "BuildTools/AutoUpdateBuilder/AutoUpdateBuilder.sln"

"${XBUILD}" /p:Configuration=Debug "BuildTools/GnupgSigningTool/GnupgSigningTool.sln"

"${XBUILD}" /p:Configuration=Release /target:Clean "Duplicati.sln"
find "Duplicati" -type d -name "Release" | xargs rm -rf
if [ ! -d "$XAMARIN" ]; then
    read -p"Warning, this build will not enable tray icon on Mac, hit any key to continue."
    "${XBUILD}" /p:DefineConstants=ENABLE_GTK /p:Configuration=Release "Duplicati.sln"
else
    "${XBUILD}" -p:DefineConstants=\"ENABLE_GTK\;XAMARIN_MAC\" /p:Configuration=Release "Duplicati.sln"
fi
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

# Fix for some support libraries not being picked up
for BACKEND in Duplicati/Library/Backend/*; do
	if [ -d "${BACKEND}/bin/Release/" ]; then
		cp "${BACKEND}/bin/Release/"*.dll "${UPDATE_SOURCE}"
	fi
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
			SIGN_MSG=$(osslsigncode sign -pkcs12 "${AUTHENTICODE_PFXFILE}" -pass "${PFX_PASS}" -n "Duplicati" -i "http://www.duplicati.com" -h "${hashalg}" ${NEST} -t "http://timestamp.digicert.com?alg=${hashalg}" -in "$1" -out tmpfile)
			if [ "${SIGN_MSG}" != "Succeeded" ]; then echo "${SIGN_MSG}"; fi
			mv tmpfile "$1"
			NEST="-nest"
		done
	}

	PFX_PASS=$("${MONO}" "BuildTools/AutoUpdateBuilder/bin/Debug/SharpAESCrypt.exe" d "${KEYFILE_PASSWORD}" "${AUTHENTICODE_PASSWORD}")

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

"${MONO}" "BuildTools/AutoUpdateBuilder/bin/Debug/AutoUpdateBuilder.exe" --input="${UPDATE_SOURCE}" \
--output="${UPDATE_TARGET}" --keyfile="${UPDATER_KEYFILE}" \
--manifest=Updates/${RELEASE_TYPE}.manifest --changeinfo="${RELEASE_CHANGEINFO}" \
--displayname="${RELEASE_NAME}" --remoteurls="${UPDATE_ZIP_URLS}" \
--version="${RELEASE_VERSION}" --keyfile-password="${KEYFILE_PASSWORD}"

if [ ! -f "${UPDATE_TARGET}/package.zip" ]; then
	"${MONO}" "BuildTools/UpdateVersionStamp/bin/Release/UpdateVersionStamp.exe" --version="${RELEASE_VERSION}"

	echo "Something went wrong while building the package, no output found"
	exit 5
fi

"${MONO}" "BuildTools/GnupgSigningTool/bin/Debug/GnupgSigningTool.exe" \
--inputfile=\"${UPDATE_TARGET}/package.zip\" \
--signaturefile=\"${UPDATE_TARGET}/package.zip.sig\" \
--armor=false --gpgkeyfile="${GPG_KEYFILE}" --gpgpath="${GPG}" \
--keyfile-password="${KEYFILE_PASSWORD}"

"${MONO}" "BuildTools/GnupgSigningTool/bin/Debug/GnupgSigningTool.exe" \
--inputfile=\"${UPDATE_TARGET}/package.zip\" \
--signaturefile=\"${UPDATE_TARGET}/package.zip.sig.asc\" \
--armor=true --gpgkeyfile="${GPG_KEYFILE}" --gpgpath="${GPG}" \
--keyfile-password="${KEYFILE_PASSWORD}"

echo "${RELEASE_INC_VERSION}" > "Updates/build_version.txt"

mv "${UPDATE_TARGET}/package.zip" "${UPDATE_TARGET}/latest.zip"
mv "${UPDATE_TARGET}/autoupdate.manifest" "${UPDATE_TARGET}/latest.manifest"
mv "${UPDATE_TARGET}/package.zip.sig" "${UPDATE_TARGET}/latest.zip.sig"
mv "${UPDATE_TARGET}/package.zip.sig.asc" "${UPDATE_TARGET}/latest.zip.sig.asc"
cp "${UPDATE_TARGET}/latest.zip" "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip"
cp "${UPDATE_TARGET}/latest.manifest" "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.manifest"
cp "${UPDATE_TARGET}/latest.zip.sig" "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip.sig"
cp "${UPDATE_TARGET}/latest.zip.sig.asc" "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip.sig.asc"

"${MONO}" "BuildTools/UpdateVersionStamp/bin/Release/UpdateVersionStamp.exe" --version="${RELEASE_VERSION}"

echo "Uploading binaries"
"${AWS}" --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip" "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip"
"${AWS}" --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip.sig" "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig"
"${AWS}" --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip.sig.asc" "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig.asc"
"${AWS}" --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.manifest" "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.manifest"

"${AWS}" --profile=duplicati-upload s3 cp "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.manifest" "s3://updates.duplicati.com/${RELEASE_TYPE}/latest.manifest"

ZIP_MD5=$(md5 ${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip | awk -F ' ' '{print $NF}')
ZIP_SHA1=$(shasum -a 1 ${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip | awk -F ' ' '{print $1}')
ZIP_SHA256=$(shasum -a 256 ${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip | awk -F ' ' '{print $1}')

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

"${AWS}" --profile=duplicati-upload s3 cp "latest.json" "s3://updates.duplicati.com/${RELEASE_TYPE}/latest.json"
"${AWS}" --profile=duplicati-upload s3 cp "latest.js" "s3://updates.duplicati.com/${RELEASE_TYPE}/latest.js"

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

RELEASE_MESSAGE=$(printf "Changes in this version:\n${RELEASE_CHANGEINFO_NEWS}")

GITHUB_TOKEN=$(cat "${GITHUB_TOKEN_FILE}")

if [ "x${GITHUB_TOKEN}" == "x" ]; then
	echo "No GITHUB_TOKEN found in environment, you can manually upload the binaries"
else
	"${GITHUB_RELEASE}" release ${PRE_RELEASE_LABEL} \
	    --tag "v${RELEASE_VERSION}-${RELEASE_NAME}"  \
	    --name "v${RELEASE_VERSION}-${RELEASE_NAME}" \
	    --repo "duplicati" \
	    --user "duplicati" \
	    --security-token "${GITHUB_TOKEN}" \
	    --description "${RELEASE_MESSAGE}" \

	"${GITHUB_RELEASE}" upload \
	    --tag "v${RELEASE_VERSION}-${RELEASE_NAME}"  \
	    --name "${RELEASE_FILE_NAME}.zip" \
	    --repo "duplicati" \
	    --user "duplicati" \
	    --security-token "${GITHUB_TOKEN}" \
	    --file "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip"
fi


DISCOURSE_TOKEN=$(cat "${DISCOURSE_TOKEN_FILE}")

if [ "x${DISCOURSE_TOKEN}" == "x" ]; then
	echo "No DISCOURSE_TOKEN found in environment, you can manually create the post on the forum"
else

	body="# [${RELEASE_VERSION}-${RELEASE_NAME}](https://github.com/duplicati/duplicati/releases/tag/v${RELEASE_VERSION}-${RELEASE_NAME})

${RELEASE_CHANGEINFO_NEWS}
"

	DISCOURSE_USERNAME=$(echo "${DISCOURSE_TOKEN}" | cut -d ":" -f 1)
	DISCOURSE_APIKEY=$(echo "${DISCOURSE_TOKEN}" | cut -d ":" -f 2)

	curl -X POST "https://forum.duplicati.com/posts" \
		-H "Content-Type: multipart/form-data" \
		-H "Accept: application/json" \
		-H "Api-Key: ${DISCOURSE_APIKEY}" \
		-H "Api-Username: ${DISCOURSE_USERNAME}" \
		-F "category=10" \
		-F "title=Release: ${RELEASE_VERSION} (${RELEASE_TYPE}) ${RELEASE_TIMESTAMP}" \
		-F "raw=${body}"
fi

git push

echo
echo "Built ${RELEASE_TYPE} version: ${RELEASE_VERSION} - ${RELEASE_NAME}"
echo "    in folder: ${UPDATE_TARGET}"
echo
echo
echo "Building installers ..."

# Send the password along to avoid typing it again
export KEYFILE_PASSWORD

bash "build-installers.sh" "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip"


