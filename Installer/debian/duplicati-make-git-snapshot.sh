#!/bin/sh

# Usage: ./duplicati-make-git-snapshot.sh [COMMIT] [DATE] [VERSION] [RELEASETYPE] [BUILDTAG]
#
# to make a snapshot of the given tag/branch.  Defaults to HEAD.
# Point env var REF to a local duplicati repo to reduce clone time.

if [ -z $2 ]; then
  DATE=$(date +%Y%m%d)
else
  DATE=$2
fi

if [ -z $3 ]; then
  VERSION=$(git describe --tags | cut -d '-' -f 1 | cut -d 'v' -f 2)
else
  VERSION=$3
fi

if [ -z $4 ]; then
  RELEASETYPE=$(git describe --tags | cut -d '_' -f 2)
else
  RELEASETYPE=$4
fi

if [ -z $5 ]; then
  BUILDTAG=$(git describe --tags | cut -d '-' -f 2-4)
else
  BUILDTAG=$5
fi


DIRNAME="duplicati-$VERSION"
DATE_STAMP=$(LANG=C date -R)
UPDATE_URLS="http://updates.duplicati.com/${RELEASETYPE}/latest.manifest;http://alt.updates.duplicati.com/${RELEASETYPE}/latest.manifest"

echo REF ${REF:+--reference $REF}
echo DIRNAME $DIRNAME
echo COMMIT ${1:-HEAD}
echo RELEASETYPE ${RELEASETYPE}
echo URLS ${UPDATE_URLS}
echo BUILDTAG ${BUILDTAG}


rm -rf $DIRNAME

git clone ${REF:+--reference $REF} \
         `git config --get remote.origin.url` $DIRNAME

cd "$DIRNAME"

git checkout -b fedora-build ${1:-HEAD}

echo "${BUILDTAG}" > "Duplicati/License/VersionTag.txt"
echo "${RELEASETYPE}" > "Duplicati/Library/AutoUpdater/AutoUpdateBuildChannel.txt"
echo "${UPDATE_URLS}" > "Duplicati/Library/AutoUpdater/AutoUpdateURL.txt"
cp "Updates/release_key.txt" "Duplicati/Library/AutoUpdater/AutoUpdateSignKey.txt"

git add "Duplicati/License/VersionTag.txt"
git add "Duplicati/Library/AutoUpdater/AutoUpdateBuildChannel.txt"
git add "Duplicati/Library/AutoUpdater/AutoUpdateURL.txt"
git add "Duplicati/Library/AutoUpdater/AutoUpdateSignKey.txt"
git commit -m "Updated auto-update properties"

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
            echo "Installing OEM override file"
            cp $p .
            git add ./$n
            git commit -m "Added OEM override file"
        fi
    done
done

cp -R "../debian" .

sed -e "s;%VERSION%;$VERSION;g" -e "s;%DATE%;$DATE_STAMP;g" "../debian/changelog" > "debian/changelog"

echo "${VERSION}" > version
git add version
git commit -m "Added version file"

cd ..