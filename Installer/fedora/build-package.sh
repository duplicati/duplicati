#!/bin/bash

git pull

DATE=`date +%Y%m%d`
VERSION=`git describe --tags | cut -d '-' -f 1 | cut -d 'v' -f 2`
GITTAG=`git describe --tags | cut -d '-' -f 6`

#bash duplicati-make-git-snapshot.sh "${GITTAG}" "${DATE}" "${VERSION}"
#mv duplicati-$DATE.tar.bz2 ~/rpmbuild/SOURCES/ 
cp *.sh ~/rpmbuild/SOURCES/
cp *.patch ~/rpmbuild/SOURCES/

rpmbuild -bs --define "_builddate ${DATE}" --define "_buildversion ${VERSION}" --define "_gittag ${GITTAG}" duplicati.spec
rpmbuild -bb --define "_builddate ${DATE}" --define "_buildversion ${VERSION}" --define "_gittag ${GITTAG}" duplicati.spec
