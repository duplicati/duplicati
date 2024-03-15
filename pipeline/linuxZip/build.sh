#!/bin/bash
#This is a helper to make the release linux zip. Mainly for testing outside of ci/cd

SCRIPTDIR=$( cd "$(dirname "$0")" ; pwd -P )

docker build $SCRIPTDIR/docker -t duplicati-build

export MSYS_NO_PATHCONV=1
docker run --rm -v $SCRIPTDIR/../../:/sources duplicati-build