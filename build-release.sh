quit_on_error() {
  local parent_lineno="$1"
  local message="$2"
  local code="${3:-1}"
  if [[ -n "$message" ]] ; then
    echo "Error on or near line ${parent_lineno}: ${message}; exiting with status ${code}"
  else
    echo "Error on or near line ${parent_lineno}; exiting with status ${code}"
  fi
  exit "${code}"
}

set -eE
trap 'quit_on_error $LINENO' ERR

function update_git_repo () {
	git checkout "Duplicati/License/VersionTag.txt"
	git checkout "Duplicati/Library/AutoUpdater/AutoUpdateURL.txt"
	git checkout "Duplicati/Library/AutoUpdater/AutoUpdateBuildChannel.txt"
	git add "Updates/build_version.txt"
	git add "${RELEASE_CHANGELOG_FILE}"
	git commit -m "Version bump to v${RELEASE_VERSION}-${RELEASE_NAME}" -m "You can download this build from: " -m "Binaries: https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip" -m "Signature file: https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig" -m "ASCII signature file: https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig.asc" -m "MD5: ${ZIP_MD5}" -m "SHA1: ${ZIP_SHA1}" -m "SHA256: ${ZIP_SHA256}"
	git tag "v${RELEASE_VERSION}-${RELEASE_NAME}"                       -m "You can download this build from: " -m "Binaries: https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip" -m "Signature file: https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig" -m "ASCII signature file: https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig.asc" -m "MD5: ${ZIP_MD5}" -m "SHA1: ${ZIP_SHA1}" -m "SHA256: ${ZIP_SHA256}"
	git push --tags
}

function set_keyfile_password () {
	echo -n "Enter keyfile password: "
	read -s KEYFILE_PASSWORD
	echo

	if [ "z${KEYFILE_PASSWORD}" == "z" ]; then
		echo "No password entered, quitting"
		exit 0
	fi
}

function release_to_github () {
	# Using the tool from https://github.com/aktau/github-release

	GITHUB_TOKEN_FILE="${HOME}/.config/github-api-token"
	GITHUB_TOKEN=$(cat "${GITHUB_TOKEN_FILE}")
	RELEASE_MESSAGE=$(printf "Changes in this version:\n${RELEASE_CHANGEINFO_NEWS}")

	PRE_RELEASE_LABEL="--pre-release"
	if [ "${RELEASE_TYPE}" == "stable" ]; then
		PRE_RELEASE_LABEL=""
	fi

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
}

function post_to_forum () {
	DISCOURSE_TOKEN_FILE="${HOME}/.config/discourse-api-token"
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
			-F "api_key=${DISCOURSE_APIKEY}" \
			-F "api_username=${DISCOURSE_USERNAME}" \
			-F "category=Releases" \
			-F "title=Release: ${RELEASE_VERSION} (${RELEASE_TYPE}) ${RELEASE_TIMESTAMP}" \
			-F "raw=${body}"
	fi
}

function upload_binaries_to_aws () {
	echo "Uploading binaries"
	aws --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip" "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip"
	aws --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip.sig" "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig"
	aws --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip.sig.asc" "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip.sig.asc"
	aws --profile=duplicati-upload s3 cp "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.manifest" "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.manifest"

	aws --profile=duplicati-upload s3 cp "s3://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.manifest" "s3://updates.duplicati.com/${RELEASE_TYPE}/latest.manifest"

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

	aws --profile=duplicati-upload s3 cp "latest.json" "s3://updates.duplicati.com/${RELEASE_TYPE}/latest.json"
	aws --profile=duplicati-upload s3 cp "latest.js" "s3://updates.duplicati.com/${RELEASE_TYPE}/latest.js"
}


function sign_with_authenticode () {
	AUTHENTICODE_PFXFILE="${HOME}/.config/signkeys/Duplicati/authenticode.pfx"
	AUTHENTICODE_PASSWORD="${HOME}/.config/signkeys/Duplicati/authenticode.key"

	if [ -f "${AUTHENTICODE_PFXFILE}" ] && [ -f "${AUTHENTICODE_PASSWORD}" ]; then
		echo "Performing authenticode signing of executables and libraries"

		authenticode_sign() {
			NEST=""
			for hashalg in sha1 sha256; do
				SIGN_MSG=$(osslsigncode sign -pkcs12 "${AUTHENTICODE_PFXFILE}" -pass "${PFX_PASS}" -n "Duplicati" -i "http://www.duplicati.com" -h "${hashalg}" ${NEST} -t "http://timestamp.verisign.com/scripts/timstamp.dll" -in "$1" -out tmpfile)
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
}

function prepare_update_target_folder () {
	UPDATER_KEYFILE="${HOME}/.config/signkeys/Duplicati/updater-release.key"
	UPDATE_TARGET=Updates/build/${RELEASE_TYPE}_target-${RELEASE_VERSION}
	UPDATE_ZIP_URLS="https://updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip;https://alt.updates.duplicati.com/${RELEASE_TYPE}/${RELEASE_FILE_NAME}.zip"

	if [ -e "${UPDATE_TARGET}" ]; then rm -rf "${UPDATE_TARGET}"; fi
	mkdir -p "${UPDATE_TARGET}"

	# Newer GPG needs this to allow input from a non-terminal
	export GPG_TTY=$(tty)
	GPG_KEYFILE="${HOME}/.config/signkeys/Duplicati/updater-gpgkey.key"
	GPG=/usr/local/bin/gpg2

	"${MONO}" "BuildTools/AutoUpdateBuilder/bin/Debug/AutoUpdateBuilder.exe" --input="${UPDATE_SOURCE}" --output="${UPDATE_TARGET}" --keyfile="${UPDATER_KEYFILE}" --manifest=Updates/${RELEASE_TYPE}.manifest --changeinfo="${RELEASE_CHANGEINFO}" --displayname="${RELEASE_NAME}" --remoteurls="${UPDATE_ZIP_URLS}" --version="${RELEASE_VERSION}" --keyfile-password="${KEYFILE_PASSWORD}" --gpgkeyfile="${GPG_KEYFILE}" --gpgpath="${GPG}"

	if [ ! -f "${UPDATE_TARGET}/package.zip" ]; then
		"${MONO}" "BuildTools/UpdateVersionStamp/bin/Release/UpdateVersionStamp.exe" --version="2.0.0.7"

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
}


function prepare_update_source_folder () {
	UPDATE_SOURCE=Updates/build/${RELEASE_TYPE}_source-${RELEASE_VERSION}
	if [ -e "${UPDATE_SOURCE}" ]; then rm -rf "${UPDATE_SOURCE}"; fi
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
}

function clean_and_build () {
	XBUILD=`which msbuild || /Library/Frameworks/Mono.framework/Commands/msbuild`
	NUGET=`which nuget || /Library/Frameworks/Mono.framework/Commands/nuget`
	MONO=`which mono || /Library/Frameworks/Mono.framework/Commands/mono`

	"${XBUILD}" /property:Configuration=Release "BuildTools/UpdateVersionStamp/UpdateVersionStamp.csproj"
	"${MONO}" "BuildTools/UpdateVersionStamp/bin/Release/UpdateVersionStamp.exe" --version="${RELEASE_VERSION}"

	# build autoupdate
	"${NUGET}" restore "BuildTools/AutoUpdateBuilder/AutoUpdateBuilder.sln"
	"${NUGET}" restore "Duplicati.sln"
	"${XBUILD}" /p:Configuration=Release "BuildTools/AutoUpdateBuilder/AutoUpdateBuilder.sln"

	# clean
	find "Duplicati" -type d -name "Release" | xargs rm -rf
	"${XBUILD}" /p:Configuration=Release /target:Clean "Duplicati.sln"

	"${XBUILD}" /p:DefineConstants=__MonoCS__ /p:DefineConstants=ENABLE_GTK /p:Configuration=Release "Duplicati.sln"
}

function update_text_files_with_new_version() {
	UPDATE_MANIFEST_URLS="https://updates.duplicati.com/${RELEASE_TYPE}/latest.manifest;https://alt.updates.duplicati.com/${RELEASE_TYPE}/latest.manifest"


	if [[ ! -f "${RELEASE_CHANGELOG_NEWS_FILE}" ]]; then
		echo "No updates to add to changelog found"
		echo
		echo "To make a build without changelog news, run:"
		echo "    touch ""${RELEASE_CHANGELOG_NEWS_FILE}"" "
		exit 0
	fi

	RELEASE_CHANGELOG_NEWS_FILE="changelog-news.txt" # never in repo due to .gitignore
	RELEASE_CHANGEINFO_NEWS=$(cat "${RELEASE_CHANGELOG_NEWS_FILE}" 2>/dev/null)
	if [ ! "x${RELEASE_CHANGEINFO_NEWS}" == "x" ]; then

		echo "${RELEASE_TIMESTAMP} - ${RELEASE_NAME}" > "tmp_changelog.txt"
		echo "==========" >> "tmp_changelog.txt"
		echo "${RELEASE_CHANGEINFO_NEWS}" >> "tmp_changelog.txt"
		echo >> "tmp_changelog.txt"
		cat "${RELEASE_CHANGELOG_FILE}" >> "tmp_changelog.txt"
		cp "tmp_changelog.txt" "${RELEASE_CHANGELOG_FILE}"
		rm "tmp_changelog.txt"
	fi
	rm "${RELEASE_CHANGELOG_NEWS_FILE}"

	echo "${RELEASE_NAME}" > "Duplicati/License/VersionTag.txt"
	echo "${RELEASE_TYPE}" > "Duplicati/Library/AutoUpdater/AutoUpdateBuildChannel.txt"
	echo "${UPDATE_MANIFEST_URLS}" > "Duplicati/Library/AutoUpdater/AutoUpdateURL.txt"
	cp "Updates/release_key.txt"  "Duplicati/Library/AutoUpdater/AutoUpdateSignKey.txt"

	# TODO: in case of auto releasing, put some git log in changelog.
	RELEASE_CHANGEINFO=$(cat ${RELEASE_CHANGELOG_FILE})
	if [ "x${RELEASE_CHANGEINFO}" == "x" ]; then
		echo "No information in changelog file"
		exit 0
	fi
}


#set default options
LOCAL=false
AUTO_RELEASE=false
SIGNED=true

while true ; do
    case "$1" in
    --help)
        show_help
        exit 0
        ;;
	--local)
		LOCAL=true
		;;
	--auto)
		AUTO_RELEASE=true
		;;
	--unsigned)
		SIGNED=false
		;;
    --* | -* )
        echo "unknown option $1, please use --help."
        exit 1
        ;;
    * )
		if [ "x$1" == "x" ]; then
			RELEASE_TYPE="canary"
			echo "No release type specified, using ${RELEASE_TYPE}"
			break
		else
			RELEASE_TYPE=$1
		fi
        ;;
    esac
    shift
done


RELEASE_TIMESTAMP=$(date +%Y-%m-%d)
RELEASE_INC_VERSION=$(cat Updates/build_version.txt)
RELEASE_INC_VERSION=$((RELEASE_INC_VERSION+1))
RELEASE_VERSION="2.0.4.${RELEASE_INC_VERSION}"
RELEASE_NAME="${RELEASE_VERSION}_${RELEASE_TYPE}_${RELEASE_TIMESTAMP}"
RELEASE_CHANGELOG_FILE="changelog.txt"
RELEASE_FILE_NAME="duplicati-${RELEASE_NAME}"

$LOCAL || git stash save "auto-build-${RELEASE_TIMESTAMP}"

$LOCAL || update_text_files_with_new_version

clean_and_build

prepare_update_source_folder

# Remove all .DS_Store and Thumbs.db files
find  . -type f -name ".DS_Store" | xargs rm -rf
find  . -type f -name "Thumbs.db" | xargs rm -rf

$SIGNED && set_keyfile_password

$SIGNED && sign_with_authenticode

$SIGNED && prepare_update_target_folder

"${MONO}" "BuildTools/UpdateVersionStamp/bin/Release/UpdateVersionStamp.exe" --version="2.0.0.7"

$LOCAL || upload_binaries_to_aws

$LOCAL || update_git_repo

$LOCAL || release_to_github

$LOCAL || post_to_forum

echo
echo "Built ${RELEASE_TYPE} version: ${RELEASE_VERSION} - ${RELEASE_NAME}"
echo "    in folder: ${UPDATE_TARGET}"
echo
echo
echo "Building installers ..."

# Send the password along to avoid typing it again
export KEYFILE_PASSWORD

bash "build-installers.sh" "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip"