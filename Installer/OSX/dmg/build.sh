#!/bin/bash
#This is a helper to make the macos app. It assumes a bin folder exists a the root of the project with an unzipped install

SCRIPTDIR=$( cd "$(dirname "$0")" ; pwd -P )

VERSION=`grep '<Version>' < $SCRIPTDIR/../../../Executables/net5/Duplicati.Server/Duplicati.Server.csproj | sed 's/.*<Version>\([^\.]*\.[^\.]*\.[^\.]*\).*<\/Version>.*/\1/'`
VERSION=${VERSION//$'\r\n'}
echo "Building version: ($VERSION)"
export VERSION_NUMBER=$VERSION

$SCRIPTDIR/make-dmg.sh $SCRIPTDIR/../../../bin/

