#!/bin/bash

if [ ! -f "$1" ]; then
	echo "Please provide the filename of an existing zip build as the first argument"
	exit
fi

FILENAME=`basename $1`
DIRNAME=`echo "${FILENAME}" | cut -d "_" -f 1`
VERSION=`echo "${DIRNAME}" | cut -d "-" -f 2`
BUILDDATE=`date +%Y%m%d`
BUILDTAG_RAW=`echo "${FILENAME}" | cut -d "." -f 1-4 | cut -d "-" -f 2-4`
BUILDTAG="${BUILDTAG_RAW//-}"
CWD=`pwd`

echo "BUILDTAG: ${BUILDTAG}"
echo "Version: ${VERSION}"
echo "Builddate: ${BUILDDATE}"
echo "Dirname: ${DIRNAME}"

#DIRNAME="duplicati-${BUILDDATE}"
if [ -d "${DIRNAME}" ]; then
	rm -rf "${DIRNAME}"
fi
unzip -q -d "${DIRNAME}" "$1"


cp ../debian/*-launcher.sh "${DIRNAME}"
cp ../debian/duplicati.png "${DIRNAME}"
cp ../debian/duplicati.desktop "${DIRNAME}"

for n in "../oem" "../../oem" "../../../oem"
do
    if [ -d $n ]; then
        echo "Installing OEM files"
        cp -R $n "${DIRNAME}/webroot/"
    fi
done

for n in "oem-app-name.txt" "oem-update-url.txt" "oem-update-key.txt" "oem-update-readme.txt" "oem-update-installid.txt"
do
    for p in "../$n" "../../$n" "../../../$n"
    do
        if [ -f $p ]; then
            echo "Installing OEM override file"
            cp $p "${DIRNAME}"
        fi
    done
done

tar -cjf "${DIRNAME}.tar.bz2" "${DIRNAME}"
rm -rf "${DIRNAME}"

RPMBUILD="${CWD}/${DIRNAME}-rpmbuild"
if [ -d "${RPMBUILD}" ]; then
    rm -rf "${RPMBUILD}"
fi

mkdir -p "${RPMBUILD}"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}

mv "${DIRNAME}.tar.bz2" "${RPMBUILD}/SOURCES/"
cp duplicati.xpm "${RPMBUILD}/SOURCES/"
cp make-binary-package.sh "${RPMBUILD}/SOURCES/duplicati-make-binary-package.sh"
cp duplicati-install-recursive.sh "${RPMBUILD}/SOURCES/duplicati-install-recursive.sh"
cp duplicati.service "${RPMBUILD}/SOURCES/duplicati.service"
cp duplicati.default "${RPMBUILD}/SOURCES/duplicati.default"

echo "%global _builddate ${BUILDDATE}" > "${RPMBUILD}/SOURCES/duplicati-buildinfo.spec"
echo "%global _buildversion ${VERSION}" >> "${RPMBUILD}/SOURCES/duplicati-buildinfo.spec"
echo "%global _buildtag ${BUILDTAG}" >> "${RPMBUILD}/SOURCES/duplicati-buildinfo.spec"

docker build -t "duplicati/fedora-build:latest" - < Dockerfile.build

# Weirdness with time not being synced in Docker instance
sleep 5
docker run  \
    --workdir "/buildroot" \
    --volume "${CWD}":"/buildroot":"rw" \
    --volume "${RPMBUILD}":"/root/rpmbuild":"rw" \
    "duplicati/fedora-build:latest" \
    rpmbuild -bb duplicati-binary.spec

mv "${RPMBUILD}/RPMS/noarch/"*.rpm .
rm -rf "${RPMBUILD}"
