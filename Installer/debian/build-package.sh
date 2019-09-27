#!/bin/bash

DATE=$(date +%Y%m%d)
VERSION=$(git describe --tags | cut -d '-' -f 1 | cut -d 'v' -f 2)
GITTAG=$(git rev-parse --short HEAD)
RELEASETYPE=$(git describe --tags | cut -d '_' -f 2)
BUILDTAG=$(git describe --tags | cut -d '-' -f 2-4)

DIRNAME="duplicati-$VERSION"

git pull
bash duplicati-make-git-snapshot.sh "${GITTAG}" "${DATE}" "${VERSION}" "${RELEASETYPE}" "${BUILDTAG}-${GITTAG}"

cd "$DIRNAME"
touch releasenotes.txt
rm -rf .git
dpkg-buildpackage
cd ..
