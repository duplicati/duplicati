#!/bin/bash

TAGNAME=`git describe --tags | cut -d '-' -f 1 | cut -d 'v' -f 2`

DIRNAME="duplicati-$TAGNAME"

git pull
bash duplicati-make-git-snapshot.sh "$TAGNAME"

cd "$DIRNAME"
touch releasenotes.txt
echo "${TAGNAME}" > version
dpkg-buildpackage
cd ..
