#!/bin/sh

# Usage: ./duplicati-make-git-snapshot.sh [COMMIT] [DATE]
#
# to make a snapshot of the given tag/branch.  Defaults to HEAD.
# Point env var REF to a local duplicati repo to reduce clone time.

if [ -z $2 ]; then
  DATE=`date +%Y%m%d`
else
  DATE=$2
fi

DIRNAME="duplicati-$DATE"

echo REF ${REF:+--reference $REF}
echo DIRNAME $DIRNAME
echo HEAD ${1:-HEAD}

rm -rf $DIRNAME

git clone ${REF:+--reference $REF} \
         https://code.google.com/p/duplicati/ $DIRNAME

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

git archive --format=tar --prefix=$DIRNAME/ ${1:-HEAD} \
        | bzip2 > ../$DIRNAME.tar.bz2

cd ..
rm -rf $DIRNAME

