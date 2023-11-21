#!/bin/bash

# File containing numbers
FOLDER="Additional_Files"
FILE="${FOLDER}/clients"
CONFIG_FOLDER="${FOLDER}/configs"
KEY_FOLDER="${FOLDER}/keys"

# Checking if the file exists
if [ ! -f "$FILE" ]; then
    echo "File $FILE not found."
    exit 1
fi

UPDATE_SOURCE="$1"
pushd "${UPDATE_SOURCE}"
mkdir "Build"
mv * "Build"
popd

BUILD="${UPDATE_SOURCE}/Build"

# Read the file line by line
while IFS= read -r line; do
    # Assuming each line contains a single number
    client="$line"

    config_file="${CONFIG_FOLDER}/config${client}.json"
    key_file="${KEY_FOLDER}/key${client}"

    # Check if config file exists
    if [ ! -f "$config_file" ] || [ ! -f "$key_file" ]; then
        echo "Credentials for client with number: $client not found"
        continue
    fi
    echo "Building files for client: ${client}"
    build_name="${BUILD}${client}"
    cp -r "${BUILD}" "${build_name}"
    cp "$config_file" "${build_name}/webroot/config.json"
    cp "$key_file" "${build_name}/key"
    
    pushd "${build_name}"
    7z a -tzip "../${client}_build.zip" ./*
    popd
done < "$FILE"

find "$UPDATE_SOURCE" -type d -not -name '*.zip' -exec rm -rf {} +