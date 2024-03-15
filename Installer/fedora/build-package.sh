#!/bin/bash

git pull

DATE=$(date +%Y%m%d)
VERSION=$(git describe --tags | cut -d '-' -f 1 | cut -d 'v' -f 2)
GITTAG=$(git rev-parse --short HEAD)
RELEASETYPE=$(git describe --tags | cut -d '_' -f 2)
BUILDTAG=$(git describe --tags | cut -d '-' -f 2-4)


bash duplicati-make-git-snapshot.sh "${GITTAG}" "${DATE}" "${VERSION}" "${RELEASETYPE}" "${BUILDTAG}-${GITTAG}"
mv duplicati-$DATE.tar.bz2 ~/rpmbuild/SOURCES/ 
cp *.sh ~/rpmbuild/SOURCES/
cp *.patch ~/rpmbuild/SOURCES/
cp duplicati.xpm ~/rpmbuild/SOURCES/
cp build-package.sh ~/rpmbuild/SOURCES/duplicati-build-package.sh

echo "%global _gittag ${GITTAG}" > ~/rpmbuild/SOURCES/duplicati-buildinfo.spec
echo "%global _builddate ${DATE}" >> ~/rpmbuild/SOURCES/duplicati-buildinfo.spec
echo "%global _buildversion ${VERSION}" >> ~/rpmbuild/SOURCES/duplicati-buildinfo.spec
echo "%global _releasetype ${RELEASETYPE}" >> ~/rpmbuild/SOURCES/duplicati-buildinfo.spec

rpmbuild -bs duplicati.spec
rpmbuild -bb duplicati.spec
