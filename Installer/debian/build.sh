#!/bin/bash
#This is a helper to make the release linux zip. Mainly for testing outside of ci/cd

SCRIPTDIR=$( cd "$(dirname "$0")" ; pwd -P )

docker build $SCRIPTDIR/docker -t duplicati-debian

export VERSION=`grep '<Version>' < $SCRIPTDIR/../../Duplicati/Server/Duplicati.Server.csproj | sed 's/.*<Version>\(.*\)<\/Version>/\1/'`
echo Building version: $VERSION

export MSYS_NO_PATHCONV=1
docker run --rm -eVERSION=$VERSION -v $SCRIPTDIR/../../:/sources duplicati-debian