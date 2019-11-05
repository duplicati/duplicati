#!/bin/bash

# These are set via the macos-gatekeeper file
CODESIGN_IDENTITY=
NOTARIZE_USERNAME=
NOTARIZE_PASSWORD=
GATEKEEPER_SETTINGS_FILE="${HOME}/.config/signkeys/Duplicati/macos-gatekeeper"

if [ -f "${GATEKEEPER_SETTINGS_FILE}" ]; then
    source "${GATEKEEPER_SETTINGS_FILE}"
fi

if [ ! -f "$1" ]; then
    echo "Please supply a dmg or pkg file as the first input argument"
    exit 1
fi

if [ "x${NOTARIZE_USERNAME}" != "x" ]; then
    echo "Notarizing \"$1\" for macOS Gatekeeper"
    UUIDRESP=$(xcrun altool --notarize-app --primary-bundle-id "com.duplicati.app" --username "${NOTARIZE_USERNAME}" --password "${NOTARIZE_PASSWORD}" --file "$1")

    PROBE_RESULT=$(echo "${UUIDRESP}" | grep "RequestUUID " | cut -d " " -f 1)
    if [  "x${PROBE_RESULT}" == "xRequestUUID" ]; then
        REQID=$(echo "${UUIDRESP}" | grep "RequestUUID " | cut -d " " -f 3)
    else
        echo "Notarizer response should start with RequestUUID = "
        echo "Got: "
        echo "${UUIDRESP}"
        exit 1
    fi

    # Wait for the servers to sync, so we can read the UUID
    # without this step we can get errors on the --notarization-info call
    echo "Waiting for servers to accept ${REQID}..."
    while :
    do
        # Don't hammer the server
        sleep 5

        # Query the history to avoid failures with the --notarization-info not being able to find the REQID
        NOTARESP=$(xcrun altool --notarization-history 0 --username "${NOTARIZE_USERNAME}" --password "${NOTARIZE_PASSWORD}")
        ITEMLINES=$(echo "${NOTARESP}" | grep "${REQID}" | wc -l)
        if [ "${ITEMLINES}" -eq "1" ]; then
            echo "Item ${REQID} found, waiting for completion ..."

            # Extra step: we use the history to check for completion,
            # so we can delay the call to --notarization-info as it tends to give errors
            # We could rely on just a TMPRES == "status" check
            # if the --notarization-info operation is giving random errors
            while : 
            do
                # Don't hammer the server
                sleep 5
                NOTARESP=$(xcrun altool --notarization-history 0 --username "${NOTARIZE_USERNAME}" --password "${NOTARIZE_PASSWORD}")
                TMPRESP=$(echo "${NOTARESP}" | grep "${REQID}" | cut -d " " -f 5,6)
                if [ "x${TMPRESP}" != "xin progress" ]; then

                    TMPRESP=$(echo "${NOTARESP}" | grep "${REQID}" | cut -d " " -f 5)
                    echo "Status is ${TMPRESP}"
                    break
                fi
            done

            break
        elif [ "${ITEMLINES}" -ne "0" ]; then
            echo "Failed to notarize file, response:"
            echo "${NOTASTATUS}"
            exit 1          
        fi
    done



    echo "Waiting for notarization on ${REQID}..."
    while :
    do
        NOTARESP=$(xcrun altool --notarization-info "${REQID}" --username "${NOTARIZE_USERNAME}" --password "${NOTARIZE_PASSWORD}")
        NOTACODE=$?
        NOTASTATUS=$(echo "${NOTARESP}" | grep "Status:" | cut -d ":" -f 2)
        if [ "x${NOTASTATUS}" == "x success" ]; then
            break
        elif [ "x${NOTASTATUS}" == "x invalid" ]; then
            echo "Failed to notarize file, response:"
            echo "${NOTASTATUS}"
            exit 1
        elif [ "${NOTACODE}" -ne "0" ]; then
            echo "Failed to notarize file, code ${NOTACODE}, response:"
            echo "${NOTASTATUS}"
            exit 1
        fi

        # Don't hammer the server
        sleep 5
    done

    echo "Stapling the notarized document to \"$1\" "
    xcrun stapler staple "$1"
else
    echo "No notarizer credentials supplied, skipping MacOS notarizing"
fi