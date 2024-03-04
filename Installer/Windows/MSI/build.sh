#!/bin/bash
#This is a helper to make the msi. It assumes a bin folder exists a the root of the project with an unzipped install

SCRIPTDIR=$( cd "$(dirname "$0")" ; pwd -P )

VERSION=`grep '<Version>' < $SCRIPTDIR/../../../Executables/net5/Duplicati.Server/Duplicati.Server.csproj | sed 's/.*<Version>\([^\.]*\.[^\.]*\.[^\.]*\).*<\/Version>.*/\1/'`
VERSION=${VERSION//$'\r\n'}
echo "Building version: ($VERSION)"

cd $SCRIPTDIR/../../../

go-msi make --msi `pwd`/duplicati-win-x64.msi --out `pwd`/build --version $VERSION --path Installer/Windows/MSI/wix.json --src Installer/Windows/MSI/templates