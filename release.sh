
bash "build-release.sh" "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip"

echo
echo "Built ${RELEASE_TYPE} version: ${RELEASE_VERSION} - ${RELEASE_NAME}"
echo "    in folder: ${UPDATE_TARGET}"
echo
echo
echo "Building installers ..."

# Send the password along to avoid typing it again
export KEYFILE_PASSWORD

bash "build-installers.sh" "${UPDATE_TARGET}/${RELEASE_FILE_NAME}.zip"