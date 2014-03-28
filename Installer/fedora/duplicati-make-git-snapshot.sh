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

if [ -e "./oem.js" ]; then
    echo "Installing OEM script"
    cp ./oem.js $DIRNAME/Duplicati/Server/webroot/scripts/
fi

if [ -e "./oem.css" ]; then
    echo "Installing OEM stylesheet"
    cp ./oem.css $DIRNAME/Duplicati/Server/webroot/stylesheets/
fi

echo "Building tar ball"
GIT_DIR=$DIRNAME/.git git archive --format=tar --prefix=$DIRNAME/ ${1:-HEAD} \
        | bzip2 > $DIRNAME.tar.bz2

rm -rf $DIRNAME

