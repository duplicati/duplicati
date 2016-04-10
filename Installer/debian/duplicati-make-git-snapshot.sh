#!/bin/sh

# Usage: ./duplicati-make-git-snapshot.sh [COMMIT] [DATE]
#
# to make a snapshot of the given tag/branch.  Defaults to HEAD.
# Point env var REF to a local duplicati repo to reduce clone time.

if [ -z $2 ]; then
  VERSION=`git describe --tags | cut -d '-' -f 1 | cut -d 'v' -f 2`
else
  VERSION=$2
fi

DIRNAME="duplicati-$VERSION"
DATE_STAMP=`LANG=C date -R`

echo REF ${REF:+--reference $REF}
echo DIRNAME $DIRNAME
echo HEAD ${1:-HEAD}

rm -rf $DIRNAME

git clone ${REF:+--reference $REF} \
         `git config --get remote.origin.url` $DIRNAME

cd "$DIRNAME"
if [ -d "../../oem" ]; then
    echo "Installing OEM files"
    cp -R ../../oem/* Duplicati/Server/webroot/
    git add Duplicati/Server/webroot/*
    git commit -m "Added OEM files"
fi

cp -R "Installer/debian/debian" .

sed -e "s;%VERSION%;$VERSION;g" -e "s;%DATE%;$DATE_STAMP;g" "../debian/changelog" > "debian/changelog"

echo "${VERSION}" > version
git add version
git commit -m "Added version file"

git archive --format=tar --prefix=$DIRNAME/ ${1:-HEAD} \
        | bzip2 > ../$DIRNAME.tar.bz2

cd ..


