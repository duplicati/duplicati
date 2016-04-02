#!/bin/sh
#
# DMG building script adopted from:
#   http://el-tramo.be/git/fancy-dmg/plain/Makefile
#

WC_DMG=wc.dmg
WC_DIR=wc
TEMPLATE_DMG=template.dmg
SOURCE_FILES=Duplicati.app
OUTPUT=Duplicati.dmg
UNWANTED_FILES="AlphaVSS.Common.dll AlphaFS.dll AlphaFS.dll.config AlphaVSS.Common.dll.config appindicator-sharp.dll SQLite win-tools alphavss control_dir Duplicati.sqlite run-script-example.bat lvm-scripts Duplicati.debug.log SVGIcons"

SHOW_USAGE_ERROR=


TEMPLATE_DMG_BZ2=`echo "$TEMPLATE_DMG.bz2"`
DELETE_DMG=0

if [ -f "$TEMPLATE_DMG_BZ2" ]
then
	if [ -f "$TEMPLATE_DMG" ]
	then
		rm -rf "$TEMPLATE_DMG"
	fi
	
	bzip2 --decompress --keep --quiet "$TEMPLATE_DMG_BZ2"
	DELETE_DMG=1
fi

if [ ! -f "$TEMPLATE_DMG" ]
then
	echo "Template file $TEMPLATE_DMG not found"
	exit
fi

OUTPUT="Duplicati.dmg"
VERSION_NAME="Duplicati"
if [ -e "$OUTPUT" ]
then
	rm -rf "$OUTPUT"
fi

# Remove any existing work copy
if [ -e "Duplicati.app" ]
then
	rm -rf "Duplicati.app"
fi

# Get the current binary distribution
echo "Not building, using existing build ..."
#xbuild /property:Configuration=Release /target:Clean ../../Duplicati.sln
#xbuild /property:Configuration=Release ../../Duplicati.sln

if [ ! -e ../../Duplicati/GUI/Duplicati.GUI.MacTrayIcon/bin/Release/Duplicati.GUI.MacTrayIcon.app ]; then
	echo "Please build the Duplicati.GUI.MacTrayIcon project in Release mode with Xamarin Studio"
	exit
fi

mv ../../Duplicati/GUI/Duplicati.GUI.MacTrayIcon/bin/Release/Duplicati.GUI.MacTrayIcon.app Duplicati.app
cp -R ../../Duplicati/Server/webroot Duplicati.app/Contents/MonoBundle/

if [ -e ./oem.js ]; then
    echo "Installing OEM script"
    cp ./oem.js Duplicati.app/Contents/MonoBundle/webroot/scripts/
fi

if [ -e ../oem.js ]; then
    echo "Installing OEM script"
    cp ../oem.js Duplicati.app/Contents/MonoBundle/webroot/scripts/
fi

if [ -e ./oem.css ]; then
    echo "Installing OEM stylesheet"
    cp ./oem.css Duplicati.app/Contents/MonoBundle/webroot/stylesheets/
fi

if [ -e ../oem.css ]; then
    echo "Installing OEM stylesheet"
    cp ../oem.css Duplicati.app/Contents/MonoBundle/webroot/stylesheets/
fi

# Remove some of the files that we do not like
for FILE in $UNWANTED_FILES
do
	if [ -e "Duplicati.app/$FILE" ]
	then
		rm -rf "Duplicati.app/$FILE"
	fi
done

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

mv "Duplicati.app" "$WC_DIR/Duplicati.app"
cp "Duplicati-commandline-launcher" "$WC_DIR/Duplicati.app/Contents/MacOS/duplicati-cli"
cp "Duplicati-server-launcher" "$WC_DIR/Duplicati.app/Contents/MacOS/duplicati-server"
chmod +x "$WC_DIR/Duplicati.app/Contents/MacOS/duplicati-cli"
chmod +x "$WC_DIR/Duplicati.app/Contents/MacOS/duplicati-server"


# Unmount the dmg
hdiutil detach "$WC_DIR" -quiet -force

# Compress the dmg
hdiutil convert "$WC_DMG" -quiet -format UDZO -imagekey zlib-level=9 -o "$OUTPUT"

# Clean up
rm -rf "$WC_DMG"
rm -rf "$WC_DIR"

echo "Done, created $OUTPUT"