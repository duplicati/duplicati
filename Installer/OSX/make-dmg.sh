#!/bin/sh
#
# DMG building script adopted from:
#   http://el-tramo.be/git/fancy-dmg/plain/Makefile
#

WC_DMG=wc.dmg
WC_DIR=wc
TEMPLATE_DMG=template.dmg
OUTPUT=Duplicati.dmg
UNWANTED_FILES="AlphaVSS.Common.dll AlphaFS.dll AlphaFS.dll.config AlphaVSS.Common.dll.config appindicator-sharp.dll SQLite win-tools alphavss control_dir Duplicati.sqlite Duplicati-server.sqlite run-script-example.bat lvm-scripts Duplicati.debug.log SVGIcons"

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

if [ ! -f "$1" ]
then
    echo "Please supply a packaged zip file as the first input argument"
    exit
fi

VERSION_NUMBER=`echo "$1" | awk -F- '{print $2}' | awk -F_ '{print $1}'`

VERSION_NAME="Duplicati"
if [ -e "$OUTPUT" ]
then
    rm -rf "$OUTPUT"
fi

# Remove any existing work copy
if [ -e "Duplicati.app" ]
then
    sudo rm -rf "Duplicati.app"
fi

# Create folder structure
mkdir "Duplicati.app"
mkdir "Duplicati.app/Contents"
mkdir "Duplicati.app/Contents/MacOS"
mkdir "Duplicati.app/Contents/Resources"

# Extract the zip into the Resouces folder
unzip -q "$1" -d "Duplicati.app/Contents/Resources"

# Install the Info.plist and icon
SHORT_VERSION_NUMBER=`echo ${VERSION_NUMBER} | awk -F. '{printf $1; printf "."; printf $2; printf "."; print $3}'`
PLIST=`cat "Info.plist"`
PLIST=${PLIST/!LONG_VERSION!/${VERSION_NUMBER}}
PLIST=${PLIST/!SHORT_VERSION!/${SHORT_VERSION_NUMBER}}
echo ${PLIST} > "Duplicati.app/Contents/Info.plist"
cp "Duplicati.icns" "Duplicati.app/Contents/Resources"

for n in "../oem" "../../oem" "../../../oem"
do
    if [ -d $n ]; then
        echo "Installing OEM files"
        cp -R $n Duplicati.app/Contents/Resources/webroot/
    fi
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

# Set permissions
sudo chown -R root:admin "Duplicati.app"
sudo chown -R root:wheel "daemon/com.duplicati.app.launchagent.plist"
sudo chmod -R 644 "daemon/com.duplicati.app.launchagent.plist"
sudo chmod +x daemon-scripts/postinstall
sudo chmod +x daemon-scripts/preinstall
sudo chmod +x app-scripts/postinstall
sudo chmod +x app-scripts/preinstall

# Make a PKG file, commented out lines can be uncommented to re-generate the lists
#pkgbuild --analyze --root "./Duplicati.app" --install-location /Applications/Duplicati.app "InstallerComponent.plist"
pkgbuild --scripts app-scripts --identifier com.duplicati.app --root "./Duplicati.app" --install-location /Applications/Duplicati.app --component-plist "InstallerComponent.plist" "DuplicatiApp.pkg"
pkgbuild --scripts daemon-scripts --identifier com.duplicati.app.daemon --root "./daemon" --install-location /Library/LaunchAgents "DuplicatiDaemon.pkg"
#productbuild --synthesize --package "DuplicatiApp.pkg" "Distribution.xml"
productbuild --distribution "Distribution.xml" --package-path "." "Duplicati.pkg"

# Alternate to allow fixing the package
#productbuild --distribution "Distribution.xml" --package-path . "DuplicatiTmp.pkg"
#pkgutil --expand "DuplicatiTmp.pkg" "DuplicatiIntermediate"
#pkgutil --flatten "DuplicatiIntermediate" "Duplicati.pkg"
#rm -rf "DuplicatiTmp.pkg"

rm -rf "DuplicatiApp.pkg"
rm -rf "DuplicatiDaemon.pkg"

# For later, sign the package as well:
#productsign --sign "Developer ID Installer: John Doe" "Duplicati.pkg" "Duplicati-signed.pkg"

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
hdiutil convert "$WC_DMG" -quiet -format UDZO -imagekey zlib-level=9 -o "$OUTPUT"

# Clean up
rm -rf "$WC_DMG"
rm -rf "$WC_DIR"

echo "Done, created $OUTPUT"