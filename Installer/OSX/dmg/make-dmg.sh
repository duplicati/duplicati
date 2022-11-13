#!/bin/sh
#
SCRIPTDIR=$( cd "$(dirname "$0")" ; pwd -P )
SRC=$1

WC_DMG=wc.dmg
WC_DIR=wc
TEMPLATE_DMG=template.dmg
OUTPUT_DMG=Duplicati.dmg
UNWANTED_FILES="win-tools control_dir Duplicati.sqlite Duplicati-server.sqlite run-script-example.bat lvm-scripts Duplicati.debug.log"

TEMPLATE_DMG_BZ2=$(echo "$SCRIPTDIR/$TEMPLATE_DMG.bz2")
DELETE_DMG=0

if [ -f "$SCRIPTDIR/$TEMPLATE_DMG_BZ2" ]; then
    if [ -f "$SCRIPTDIR/$TEMPLATE_DMG" ]; then
        rm -rf "$SCRIPTDIR/$TEMPLATE_DMG"
    fi
    
    bzip2 --decompress --keep --quiet "$SCRIPTDIR/$TEMPLATE_DMG_BZ2"
    DELETE_DMG=1
fi

if [ ! -f "$SCRIPTDIR/$TEMPLATE_DMG" ]; then
    echo "Template file $TEMPLATE_DMG not found"
    exit
fi

if [ ! -d "$SRC" ]; then
    echo "Please supply a source directory as the first argument"
    exit
fi

OUTPUT_DMG="$SRC/../$OUTPUT_DMG"

if [ -z ${VERSION_NUMBER+x} ]; then
    echo "Please set the VERSION_NUMBER environment variable before calling"
    VERSION_NUMBER=0.0.0
fi

VERSION_NAME="Duplicati"
if [ -e "${OUTPUT_DMG}" ]; then
    rm -rf "${OUTPUT_DMG}"
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
cp -r $SRC "Duplicati.app/Contents/Resources"

# Install the Info.plist and icon, patch the plist file as well
PLIST=$(cat "$SCRIPTDIR/Info.plist")
PLIST=${PLIST//!LONG_VERSION!/${VERSION_NUMBER}}
echo ${PLIST} > "Duplicati.app/Contents/Info.plist"
cp "$SCRIPTDIR/Duplicati.icns" "Duplicati.app/Contents/Resources"

chmod +x "Duplicati.app/Contents/MacOS/Duplicati.GUI.TrayIcon"

# Remove some of the files that we do not like
for FILE in $UNWANTED_FILES
do
    if [ -e "Duplicati.app/Contents/Resources/${FILE}" ]
    then
        rm -rf "Duplicati.app/Contents/Resources/${FILE}"
    fi
done

# Set permissions
sudo chown -R root:admin "Duplicati.app"

# Prepare a new dmg
echo "Building dmg"
if [ "$DELETE_DMG" -eq "1" ]
then
    # If we have just extracted the dmg, use that as working copy
    WC_DMG=$SCRIPTDIR/$TEMPLATE_DMG
else
    # Otherwise we want a copy so we kan keep the original fresh
    cp "$SCRIPTDIR/$TEMPLATE_DMG" "$WC_DMG"
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

echo "Done, created ${OUTPUT_DMG}"
