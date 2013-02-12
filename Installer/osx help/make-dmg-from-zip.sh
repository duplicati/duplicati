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
UNWANTED_FILES="sqlite-3.6.12.so sqlite3.dll linux-sqlite-readme.txt win-tools"

SHOW_USAGE_ERROR=

if [ "$#" -lt "1" ]
then
	SHOW_USAGE_ERROR="No input file given"
fi

if [ "$#" -gt "2" ]
then
	SHOW_USAGE_ERROR="Too many arguments"
fi


if [ -n "$SHOW_USAGE_ERROR" ]
then
	echo "$SHOW_USAGE_ERROR"
	echo
	echo "Usage:"
	echo "$0 \"Duplicati 1.3.zip\" [output.dmg]"
	echo
	exit
fi


if [ ! -f "$1" ]
then
	echo "Input file $1 does not exist"
	exit
fi

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

TMP_FILENAME=`basename "$1"`
OUTPUT=`echo "${TMP_FILENAME%%.zip}.dmg"`

if [ "$#" -gt "1" ]
then
	if [ -d "$2" ]
	then
		OUTPUT=`echo "${2%%/}/$OUTPUT"`
	else
		OUTPUT=$2
	fi
fi

VERSION_NAME=`basename "$OUTPUT"`
VERSION_NAME=${VERSION_NAME%%.dmg}

if [ -e "$OUTPUT" ]
then
	rm -rf "$OUTPUT"
fi


# Remove any existing work copy
if [ -e "Duplicati" ]
then
	rm -rf Duplicati
fi

# Get the current binary distribution
echo "Extracting files"
unzip -q "$1"

if [ ! -d "Duplicati" ]
then
	echo "After unzipping the file $1 there was no Duplicati directory, is the zip file correct?"
	exit
fi

# Remove some of the files that we do not like
for FILE in $UNWANTED_FILES
do
	if [ -e "Duplicati/$FILE" ]
	then
		rm -rf "Duplicati/$FILE"
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
if [ ! -d "$WC_DIR/Duplicati.app" ]
then
	mkdir "$WC_DIR/Duplicati.app"
fi

mkdir "$WC_DIR/Duplicati.app/Contents"
mkdir "$WC_DIR/Duplicati.app/Contents/Resources"
mkdir "$WC_DIR/Duplicati.app/Contents/MacOS"

# Copy in contents
cp "Info.plist" "$WC_DIR/Duplicati.app/Contents/"
cp "Duplicati-launcher" "$WC_DIR/Duplicati.app/Contents/MacOS/Duplicati"
cp -R "./Duplicati/"* "$WC_DIR/Duplicati.app/Contents/Resources"
cp "Duplicati.icns" "$WC_DIR/Duplicati.app/Contents/Resources"
chmod +x "$WC_DIR/Duplicati.app/Contents/MacOS/Duplicati"

# Unmount the dmg
hdiutil detach "$WC_DIR" -quiet -force

# Compress the dmg
hdiutil convert "$WC_DMG" -quiet -format UDZO -imagekey zlib-level=9 -o "$OUTPUT"

# Clean up
rm -rf "$WC_DMG"
rm -rf "$WC_DIR"
rm -rf "Duplicati"

echo "Done, created $OUTPUT"