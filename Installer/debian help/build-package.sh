#!/bin/bash

TAGNAME=`git describe --tags | cut -d '-' -f 1 | cut -d 'v' -f 2`

DIRNAME="duplicati-$TAGNAME"

git pull
bash duplicati-make-git-snapshot.sh "$TAGNAME"

cd "$DIRNAME"
touch releasenotes.txt
dpkg-buildpackage
cd ..
