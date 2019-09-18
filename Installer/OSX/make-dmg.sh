#!/bin/sh
#
# DMG building script adopted from:
#   http://el-tramo.be/git/fancy-dmg/plain/Makefile
#

WC_DMG=wc.dmg
WC_DIR=wc
TEMPLATE_DMG=template.dmg
OUTPUT_DMG=Duplicati.dmg
OUTPUT_PKG=Duplicati.pkg
UNWANTED_FILES="AlphaVSS.Common.dll AlphaFS.dll AlphaFS.dll.config AlphaVSS.Common.dll.config appindicator-sharp.dll SQLite win-tools alphavss control_dir Duplicati.sqlite Duplicati-server.sqlite run-script-example.bat lvm-scripts Duplicati.debug.log SVGIcons"

# These are set via the macos-gatekeeper file
CODESIGN_IDENTITY=
NOTARIZE_USERNAME=
NOTARIZE_PASSWORD=
GATEKEEPER_SETTINGS_FILE="${HOME}/.config/signkeys/Duplicati/macos-gatekeeper"

if [ -f "${GATEKEEPER_SETTINGS_FILE}" ]; then
    source "${GATEKEEPER_SETTINGS_FILE}"
fi


TEMPLATE_DMG_BZ2=$(echo "$TEMPLATE_DMG.bz2")
DELETE_DMG=0

if [ -f "$TEMPLATE_DMG_BZ2" ]; then
    if [ -f "$TEMPLATE_DMG" ]; then
        rm -rf "$TEMPLATE_DMG"
    fi
    
    bzip2 --decompress --keep --quiet "$TEMPLATE_DMG_BZ2"
    DELETE_DMG=1
fi

if [ ! -f "$TEMPLATE_DMG" ]; then
    echo "Template file $TEMPLATE_DMG not found"
    exit
fi

if [ ! -f "$1" ]; then
    echo "Please supply a packaged zip file as the first input argument"
    exit
fi

ZIPNAME=$(basename "$1")
VERSION_NUMBER=$(echo "$ZIPNAME" | awk -F- '{print $2}' | awk -F_ '{print $1}')

VERSION_NAME="Duplicati"
if [ -e "${OUTPUT_DMG}" ]; then
    rm -rf "${OUTPUT_DMG}"
fi

if [ -e "${OUTPUT_PKG}" ]; then
    rm -rf "${OUTPUT_PKG}"
fi

# Remove any existing work copy
if [ -e "Duplicati.app" ]; then
    sudo rm -rf "Duplicati.app"
fi

# Create folder structure
mkdir "Duplicati.app"
mkdir "Duplicati.app/Contents"
mkdir "Duplicati.app/Contents/MacOS"
mkdir "Duplicati.app/Contents/Resources"

# Extract the zip into the Resouces folder
unzip -q "$1" -d "Duplicati.app/Contents/Resources"

# Install the Info.plist and icon, patch the plist file as well
PLIST=$(cat "Info.plist")
PLIST=${PLIST//!LONG_VERSION!/${VERSION_NUMBER}}
echo ${PLIST} > "Duplicati.app/Contents/Info.plist"
cp "Duplicati.icns" "Duplicati.app/Contents/Resources"

for n in "../oem" "../../oem" "../../../oem"
do
    if [ -d $n ]; then
        echo "Installing OEM files"
        cp -R $n Duplicati.app/Contents/Resources/webroot/
    fi
done

for n in "oem-app-name.txt" "oem-update-url.txt" "oem-update-key.txt" "oem-update-readme.txt" "oem-update-installid.txt"
do
    for p in "../$n" "../../$n" "../../../$n"
    do
        if [ -f $p ]; then
            echo "Installing OEM override file"
            cp $p Duplicati.app/Contents/Resources
        fi
    done
done


# Install the LauncAgent if anyone needs it
cp -R "daemon" "Duplicati.app/Contents/Resources"

# Install executables
cp "run-with-mono.sh" "Duplicati.app/Contents/MacOS/"
cp "Duplicati-trayicon-launcher" "Duplicati.app/Contents/MacOS/duplicati"
cp "Duplicati-commandline-launcher" "Duplicati.app/Contents/MacOS/duplicati-cli"
cp "Duplicati-server-launcher" "Duplicati.app/Contents/MacOS/duplicati-server"
cp "uninstall.sh" "Duplicati.app/Contents/MacOS/"

chmod +x "Duplicati.app/Contents/MacOS/run-with-mono.sh"
chmod +x "Duplicati.app/Contents/MacOS/duplicati"
chmod +x "Duplicati.app/Contents/MacOS/duplicati-cli"
chmod +x "Duplicati.app/Contents/MacOS/duplicati-server"
chmod +x "Duplicati.app/Contents/MacOS/uninstall.sh"

# Remove some of the files that we do not like
for FILE in $UNWANTED_FILES
do
    if [ -e "Duplicati.app/Contents/Resources/${FILE}" ]
    then
        rm -rf "Duplicati.app/Contents/Resources/${FILE}"
    fi
done

# Codesign the app bundle
if [ "x${CODESIGN_IDENTITY}" != "x" ]; then
    echo "Codesigning application bundle"

    #Do a poke to get sudo prompt up before the long-running sign-process
    UNUSED=$(sudo ls)

    # Codesign all resources in bundle (i.e. the actual code)
    # Not required, but nice-to-have
    find "Duplicati.app/Contents/Resources" -type f -print0 | xargs -0 codesign -s "${CODESIGN_IDENTITY}"

    # These files have dependencies, so we need to sign them in the correct order
    for file in "duplicati-cli" "duplicati-server" "run-with-mono.sh" "uninstall.sh"; do
        codesign -s "${CODESIGN_IDENTITY}" "Duplicati.app/Contents/MacOS/${file}"
    done

    # Then sign the whole package
    codesign -s "${CODESIGN_IDENTITY}" "Duplicati.app"
else
    echo "No codesign identity supplied, skipping bundle signing"
fi

# Set permissions
sudo chown -R root:admin "Duplicati.app"
sudo chown -R root:wheel "daemon/com.duplicati.app.launchagent.plist"
sudo chmod -R 644 "daemon/com.duplicati.app.launchagent.plist"
sudo chmod +x daemon-scripts/postinstall
sudo chmod +x daemon-scripts/preinstall
sudo chmod +x app-scripts/postinstall
sudo chmod +x app-scripts/preinstall


if [ -f "DuplicatiApp.pkg" ]; then
    rm -rf "DuplicatiApp.pkg"
fi

if [ -f "DuplicatiDaemon.pkg" ]; then
    rm -rf "DuplicatiDaemon.pkg"
fi

# Make a PKG file, commented out lines can be uncommented to re-generate the lists
#pkgbuild --analyze --root "./Duplicati.app" --install-location /Applications/Duplicati.app "InstallerComponent.plist"
pkgbuild --scripts app-scripts --identifier com.duplicati.app --root "./Duplicati.app" --install-location /Applications/Duplicati.app --component-plist "InstallerComponent.plist" "DuplicatiApp.pkg"
pkgbuild --scripts daemon-scripts --identifier com.duplicati.app.daemon --root "./daemon" --install-location /Library/LaunchAgents "DuplicatiDaemon.pkg"

#productbuild --synthesize --package "DuplicatiApp.pkg" "Distribution.xml"
productbuild --distribution "Distribution.xml" --package-path "." --resources "." "${OUTPUT_PKG}"

# Alternate to allow fixing the package
#productbuild --distribution "Distribution.xml" --package-path . "DuplicatiTmp.pkg"
#pkgutil --expand "DuplicatiTmp.pkg" "DuplicatiIntermediate"
#pkgutil --flatten "DuplicatiIntermediate" "Duplicati.pkg"
#rm -rf "DuplicatiTmp.pkg"

rm -rf "DuplicatiApp.pkg"
rm -rf "DuplicatiDaemon.pkg"

if [ "x${CODESIGN_IDENTITY}" != "x" ]; then
    echo "Codesigning installer package"
    productsign --sign "${CODESIGN_IDENTITY}" "${OUTPUT_PKG}" "${OUTPUT_PKG}.signed"
    mv "${OUTPUT_PKG}.signed" "${OUTPUT_PKG}"
else
    echo "No codesign identity supplied, skipping package signing"
fi

# Prepare a new dmg
echo "Building dmg"
if [ "$DELETE_DMG" -eq "1" ]
then
    # If we have just extracted the dmg, use that as working copy
    WC_DMG=$TEMPLATE_DMG
else
    # Otherwise we want a copy so we kan keep the original fresh
    cp "$TEMPLATE_DMG" "$WC_DMG"
fi

# Make a mount point and mount the new dmg
mkdir -p "$WC_DIR"
hdiutil attach "$WC_DMG" -noautoopen -quiet -mountpoint "$WC_DIR"

# Change the dmg name
echo "Setting dmg name to $VERSION_NAME"
diskutil quiet rename wc "$VERSION_NAME"

# Make the Duplicati.app structure, root folder should exist
if [ -e "$WC_DIR/Duplicati.app" ]
then
    rm -rf "$WC_DIR/Duplicati.app"
fi

# Move in the prepared folder
sudo mv "Duplicati.app" "$WC_DIR/Duplicati.app"

# Unmount the dmg
hdiutil detach "$WC_DIR" -quiet -force

# Compress the dmg
hdiutil convert "$WC_DMG" -quiet -format UDZO -imagekey zlib-level=9 -o "${OUTPUT_DMG}"

# Clean up
rm -rf "$WC_DMG"
rm -rf "$WC_DIR"

if [ "x${CODESIGN_IDENTITY}" != "x" ]; then
    echo "Codesigning DMG image"
    codesign -s "${CODESIGN_IDENTITY}" "${OUTPUT_DMG}"
else
    echo "No codesign identity supplied, skipping DMG signing"
fi

if [ "x${NOTARIZE_USERNAME}" != "x" ]; then
    echo "Notarizing pkg package for MacOS Gatekeeper"
    xcrun altool --notarize-app --primary-bundle-id "com.duplicati.app" --username "{NOTARIZE_USERNAME}" --password "{NOTARIZE_PASSWORD}" --file "${OUTPUT_PKG}"
    echo "Notarizing dmg package for MacOS Gatekeeper"
    xcrun altool --notarize-app --primary-bundle-id "com.duplicati.app" --username "{NOTARIZE_USERNAME}" --password "{NOTARIZE_PASSWORD}" --file "${OUTPUT_DMG}"

    # We want to notarize the builds, but the delay is more than one hour,
    # so we would need to wait for the signing to complete before we
    # can staple and compute the hash/signature of the archive
    
    #echo "Stapling the notarized document to the pkg package"
    #xcrun stapler staple "{OUTPUT_PKG}"
    #echo "Stapling the notarized document to the dmg package"
    #xcrun stapler staple "{OUTPUT_DMG}"

else
    echo "No notarizer credentials supplied, skipping MacOS notarizing"
fi

echo "Done, created ${OUTPUT_DMG}"