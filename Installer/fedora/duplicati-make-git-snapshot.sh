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
         https://github.com/duplicati/duplicati.git $DIRNAME

cd "$DIRNAME"
for n in "../../oem" "../../../oem" "../../../../oem"
do
    if [ -d $n ]; then
        echo "Installing OEM files"
        cp -R $n Duplicati/Server/webroot/
        git add Duplicati/Server/webroot/*
        git commit -m "Added OEM files"
    fi
done

for n in "oem-app-name.txt" "oem-update-url.txt" "oem-update-key.txt" "oem-update-readme.txt" "oem-update-installid.txt"
do
    for p in "../../$n" "../../../$n" "../../../../$n"
    do
        if [ -f $p ]; then
            echo "Installin OEM override file"
            cp $p .
            git add ./$n
            git commit -m "Added OEM override file"
        fi
    done
done


git archive --format=tar --prefix=$DIRNAME/ ${1:-HEAD} \
        | bzip2 > ../$DIRNAME.tar.bz2

cd ..
rm -rf $DIRNAME

