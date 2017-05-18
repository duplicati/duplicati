#!/bin/bash

git pull

DATE=`date +%Y%m%d`
VERSION=`git describe --tags | cut -d '-' -f 1 | cut -d 'v' -f 2`
GITTAG=`git rev-parse --short HEAD`
RELEASETYPE=`git describe --tags | cut -d '_' -f 2`
BUILDTAG=`git describe --tags | cut -d '-' -f 2-4`
CWD=`pwd`

bash duplicati-make-git-snapshot.sh "${GITTAG}" "${DATE}" "${VERSION}" "${RELEASETYPE}" "${BUILDTAG}-${GITTAG}"

RPMBUILD="${CWD}/${BUILDTAG}-rpmbuild"
if [ -d "${RPMBUILD}" ]; then
    rm -rf "${RPMBUILD}"
fi

mkdir -p "${RPMBUILD}"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}

mv duplicati-$DATE.tar.bz2 "${RPMBUILD}/SOURCES/"
cp *.sh "${RPMBUILD}/SOURCES/"
cp *.patch "${RPMBUILD}/SOURCES/"
cp duplicati.xpm "${RPMBUILD}/SOURCES/"
cp build-package.sh "${RPMBUILD}/SOURCES/duplicati-build-package.sh"

echo "%global _gittag ${GITTAG}" > "${RPMBUILD}/SOURCES/duplicati-buildinfo.spec"
echo "%global _builddate ${DATE}" >> "${RPMBUILD}/SOURCES/duplicati-buildinfo.spec"
echo "%global _buildversion ${VERSION}" >> "${RPMBUILD}/SOURCES/duplicati-buildinfo.spec"
echo "%global _releasetype ${RELEASETYPE}" >> "${RPMBUILD}/SOURCES/duplicati-buildinfo.spec"

docker build -t "duplicati/fedora-build:latest" - < Dockerfile.build

# Weirdness with time not being synced in Docker instance
sleep 5
docker run  \
    --workdir "/buildroot" \
    --volume "${CWD}":"/buildroot":"rw" \
    --volume "${RPMBUILD}":"/root/rpmbuild":"rw" \
    "duplicati/fedora-build:latest" \
    rpmbuild -bs duplicati.spec

docker run  \
    --workdir "/buildroot" \
    --volume "${CWD}":"/buildroot":"rw" \
    --volume "${RPMBUILD}":"/root/rpmbuild":"rw" \
    "duplicati/fedora-build:latest" \
    rpmbuild -bb duplicati.spec

mv "${RPMBUILD}/RPMS/noarch/"*.rpm .
mv "${RPMBUILD}/SRPMS/"*.rpm .
rm -rf "${RPMBUILD}"
