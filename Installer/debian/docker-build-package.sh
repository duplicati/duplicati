#!/bin/bash

DATE=`date +%Y%m%d`
VERSION=`git describe --tags | cut -d '-' -f 1 | cut -d 'v' -f 2`
GITTAG=`git rev-parse --short HEAD`
RELEASETYPE=`git describe --tags | cut -d '_' -f 2`
BUILDTAG=`git describe --tags | cut -d '-' -f 2-4`

DIRNAME="duplicati-$VERSION"
CWD=`pwd`

git pull
bash duplicati-make-git-snapshot.sh "${GITTAG}" "${DATE}" "${VERSION}" "${RELEASETYPE}" "${BUILDTAG}-${GITTAG}"

touch "${DIRNAME}/releasenotes.txt"
rm -rf "${DIRNAME}/.git"

docker build -t "duplicati/debian-build:latest" - < Dockerfile.build

# Weirdness with time not being synced in Docker instance
sleep 5
docker run  --workdir "/buildroot/${DIRNAME}" --volume "${CWD}":"/buildroot":"rw" "duplicati/debian-build:latest" dpkg-buildpackage

rm -rf "${DIRNAME}"