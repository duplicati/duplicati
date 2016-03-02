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
if [ -e "../oem.js" ]; then
    echo "Installing OEM script"
    cp ../oem.js Duplicati/Server/webroot/scripts/
    git add Duplicati/Server/webroot/scripts/oem.js
    git commit -m "Added OEM branding script"
fi

if [ -e "../oem.css" ]; then
    echo "Installing OEM stylesheet"
    cp ../oem.css Duplicati/Server/webroot/stylesheets/
    git add Duplicati/Server/webroot/stylesheets/oem.css
    git commit -m "Added OEM branding stylesheet"
fi

cp -R "Installer/debian help/debian" .

sed -e "s;%VERSION%;$VERSION;g" -e "s;%DATE%;$DATE_STAMP;g" "../debian/changelog" > "debian/changelog"

echo "${VERSION}" > version
git add version
git commit -m "Added version file"

git archive --format=tar --prefix=$DIRNAME/ ${1:-HEAD} \
        | bzip2 > ../$DIRNAME.tar.bz2

cd ..


