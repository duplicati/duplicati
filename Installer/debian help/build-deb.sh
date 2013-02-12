#!/bin/bash

# Clean the package a bit
UNWANTED_FILES="sqlite3.dll win-tools sqlite-3.6.12.so StartDuplicati.sh"

#Validate input
SHOW_USAGE_ERROR=

if [ "$#" -lt "1" ]; then
	SHOW_USAGE_ERROR="No input file given"
fi

if [ "$#" -gt "2" ]; then
	SHOW_USAGE_ERROR="Too many arguments"
fi


if [ -n "$SHOW_USAGE_ERROR" ]; then
	echo "$SHOW_USAGE_ERROR"
	echo
	echo "Usage:"
	echo "$0 \"Duplicati 1.3.zip\" [output.deb]"
	echo
	exit
fi

if [[ $EUID -ne 0 ]]; then
   #echo "This script must be run as root, invoking as fakeroot" 1>&2
   fakeroot "$0" "$@"
   exit 1
fi

if [ ! -f "$1" ]; then
	echo "Input file $1 does not exist"
	exit
fi

# Build a debianized version number and filename
VERSION=`basename "$1"`
VERSION=${VERSION%%.zip}
VERSION=${VERSION##Duplicati }

VERSION_FILENAME="${VERSION// /-}"
VERSION_FILENAME="${VERSION_FILENAME//(/}"
VERSION_FILENAME="${VERSION_FILENAME//)/}"
ROOT_DIR="duplicati_$VERSION_FILENAME"
BUILD_TIME=`date -R`
PWD=`pwd`

# Figure out what the output file is supposed to be
OUTPUT="$ROOT_DIR.deb"
if [ "$#" -gt "1" ]; then
	if [ -d "$2" ]; then
		OUTPUT="${2%%/}/$OUTPUT"
	else
		OUTPUT=$2
	fi
else
	if [ -d "output" ]; then
		rm -rf output
	fi
	mkdir "output"
	OUTPUT="output/$OUTPUT"
fi

OUTPUT_RPM="duplicati-$VERSION_FILENAME-1.noarch.rpm"
OUTPUT_SLP="duplicati-$VERSION_FILENAME.slp"
OUTPUT_LSB="lsb-duplicati-$VERSION_FILENAME-1.noarch.rpm"
OUTPUT_TGZ="duplicati-$VERSION_FILENAME.tgz"

OUTPUT_DIR=`dirname "$OUTPUT"`

# Remove existing data so we are sure we get a clean build
if [ -f "$OUTPUT" ]; then
	rm "$OUTPUT"
fi

if [ -d "Duplicati" ]; then
	rm -rf "Duplicati"
fi

if [ -d "$ROOT_DIR" ]; then
	rm -rf "$ROOT_DIR"
fi

if [ -f "$OUTPUT_RPM" ]; then
	rm "$OUTPUT_RPM"
fi

if [ -f "$OUTPUT_SLP" ]; then
	rm "$OUTPUT_SLP"
fi

if [ -f "$OUTPUT_LSB" ]; then
	rm "$OUTPUT_LSB"
fi

if [ -f "$OUTPUT_TGZ" ]; then
	rm "$OUTPUT_TGZ"
fi

if [ -d "duplicati-$VERSION_FILENAME" ]; then
	rm -rf "duplicati-$VERSION_FILENAME"
fi

# Remove the output dir, but not if it is the source dir!
if [ -d "$OUTPUT_DIR" ] && [ "$OUTPUR_DIR" != "." ] && [ "$OUTPUT_DIR" != "$PWD" ]; then
	rm -rf "OUTPUT_DIR"
fi

# Get the binary distribution files
unzip -q "$1"

# Remove some of the files that we do not like
for FILE in $UNWANTED_FILES
do
	if [ -e "Duplicati/$FILE" ]
	then
		rm -rf "Duplicati/$FILE"
	fi
done

# Make a directory structure
mkdir "$ROOT_DIR"
mkdir "$ROOT_DIR/usr"
mkdir "$ROOT_DIR/usr/lib"
mkdir "$ROOT_DIR/usr/bin"
mkdir "$ROOT_DIR/usr/share"
mkdir "$ROOT_DIR/usr/share/applications"
mkdir "$ROOT_DIR/usr/share/pixmaps"
mkdir "$ROOT_DIR/usr/share/doc"
mkdir "$ROOT_DIR/usr/share/doc/duplicati"

# Place binary contents in the lib/duplicati dir
mv "Duplicati" "$ROOT_DIR/usr/lib/duplicati"

# Insert pacakage control files
cp -R DEBIAN "$ROOT_DIR/"
rm -rf "$ROOT_DIR/DEBIAN/.svn"
mv "$ROOT_DIR/usr/lib/duplicati/linux-readme.txt" "$ROOT_DIR/usr/share/doc/duplicati/README"
echo "This package was debianized by Kenneth Skovhede <opensource@hexad.dk> on $BUILD_TIME." > "$ROOT_DIR/usr/share/doc/duplicati/copyright"
cat "copyright" >> "$ROOT_DIR/usr/share/doc/duplicati/copyright"

# Generate a plausible changelog
echo "duplicati ($VERSION_FILENAME) unstable; urgency=low" > "$ROOT_DIR/usr/share/doc/duplicati/changelog"
echo "" >> "$ROOT_DIR/usr/share/doc/duplicati/changelog"
echo "  * Duplicati binary release $VERSION" >> "$ROOT_DIR/usr/share/doc/duplicati/changelog"
echo "" >> "$ROOT_DIR/usr/share/doc/duplicati/changelog"
echo " -- Kenneth Skovhede <opensource@hexad.dk>  $BUILD_TIME" >> "$ROOT_DIR/usr/share/doc/duplicati/changelog"
echo "" >> "$ROOT_DIR/usr/share/doc/duplicati/changelog"

cp "$ROOT_DIR/usr/share/doc/duplicati/changelog" "$ROOT_DIR/usr/share/doc/duplicati/changelog.Debian"

gzip --quiet --best "$ROOT_DIR/usr/share/doc/duplicati/changelog"
gzip --quiet --best "$ROOT_DIR/usr/share/doc/duplicati/changelog.Debian"

# Make sym link to install launcher scripts in /usr/bin
# We install these before calculating the size
cp "duplicati-launcher.sh" "$ROOT_DIR/usr/bin/duplicati"
cp "duplicati-commandline-launcher.sh" "$ROOT_DIR/usr/bin/duplicati-commandline"
cp "duplicati.desktop" "$ROOT_DIR/usr/share/applications/"
cp "duplicati.xpm" "$ROOT_DIR/usr/share/pixmaps/"
cp "duplicati.png" "$ROOT_DIR/usr/share/pixmaps/"

# If we edit files, we sometimes get backup files included
find "$ROOT_DIR" -type f -name \*\~ -exec rm -rf '{}' \;

# Calculate install size in KB
INSTALL_SIZE=`du -sx --exclude DEBIAN`
INSTALL_SIZE=${INSTALL_SIZE%%.}

# We also need to update the control version number, but this file is too
# big to have in echo format, so we read the lines
CONTROL_FILE="$ROOT_DIR/DEBIAN/control"
rm "$CONTROL_FILE"

# Match only newlines
OLD_IFS=$IFS    
IFS=$'\n'
for LINE in `cat "DEBIAN/control"`; do
	# If line starts with "Version: " or "Installed-Size", we replace it,
	# otherwise we pass in unchanged	
	TST=${LINE##Version: }
	TST2=${LINE##Installed-Size: }
	if [ "$TST" != "$LINE" ]; then
		echo "Version: $VERSION_FILENAME" >> "$CONTROL_FILE"
	elif [ "$TST2" != "$LINE" ]; then
		echo "Installed-Size: $INSTALL_SIZE" >> "$CONTROL_FILE"
	else
		echo "$LINE" >> "$CONTROL_FILE"
	fi
done

# Restore IFS, in case we need it later
IFS=$OLD_IFS

# Fix up permissions
chown -R root:root "$ROOT_DIR"
chmod -R 755 "$ROOT_DIR"

# Match only newlines
OLD_IFS=$IFS    
IFS=$'\n'

# Set file permissions
for FILE in `find -type d`; do
	chmod 755 "$FILE"
done

for FILE in `find -type f`; do
	chmod 644 "$FILE"
done

for EXT in exe py sh; do
	for FILE in `find -type f -name \*.$EXT`; do
		chmod 755 "$FILE"
	done
done

# Restore IFS, in case we need it later
IFS=$OLD_IFS

# Make sure the actual entry points are executalbe
chmod +x "$ROOT_DIR/usr/bin/duplicati"
chmod +x "$ROOT_DIR/usr/bin/duplicati-commandline"

# Build MD5 sums
cd "$ROOT_DIR"
find usr -type f -exec md5sum '{}' \; > "DEBIAN/md5sums"
cd ..

chmod -R 644 "$ROOT_DIR/DEBIAN/md5sums"
chmod +x "$ROOT_DIR/DEBIAN/postinst"
chmod +x "$ROOT_DIR/DEBIAN/postrm"

# We should now have the correct structure in our folder, so we now build the .deb
dpkg --build "$ROOT_DIR" "$OUTPUT"

# Clean up the temp dir
rm -rf "$ROOT_DIR"

LINTIAN_OUTPUT=`lintian --allow-root --suppress-tags extra-license-file,maintainer-script-empty,binary-without-manpage,python-script-but-no-python-dep "$OUTPUT"`

if [ "$LINTIAN_OUTPUT" != "" ]; then
	echo ""
	echo "Warning: lintian found a problem with the package:"
	echo ""
	echo "$LINTIAN_OUTPUT"
	echo ""
fi

# Convert to other package formats as well
cd "$OUTPUT_DIR"
DEB_NAME=`basename "$OUTPUT"`
alien --scripts --to-rpm --keep-version "$DEB_NAME"
alien --scripts --to-slp --keep-version "$DEB_NAME"
alien --scripts --to-lsb --keep-version "$DEB_NAME"
alien --scripts --to-tgz --keep-version "$DEB_NAME"
# alien --scripts --to-pkg --keep-version "$OUTPUT"
cd "$PWD"
