#!/bin/bash

if [ ! -f "$1" ]; then
	echo "Please provide the filename of an existing zip build as the first argument"
	exit
fi

FILENAME=`basename $1`
DIRNAME=`echo "${FILENAME}" | cut -d "_" -f 1`
VERSION=`echo "${DIRNAME}" | cut -d "-" -f 2`
DATE_STAMP=`LANG=C date -R`

if [ -d "${DIRNAME}" ]; then
	rm -rf "${DIRNAME}"
fi

unzip -d "${DIRNAME}" "$1"

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

cp -R "debian" "${DIRNAME}"
cp "bin-rules.sh" "${DIRNAME}/debian/rules"
sed -e "s;%VERSION%;$VERSION;g" -e "s;%DATE%;$DATE_STAMP;g" "debian/changelog" > "${DIRNAME}/debian/changelog"

touch "${DIRNAME}/releasenotes.txt"

docker build -t "duplicati/debian-build:latest" - < Dockerfile.build

# Weirdness with time not being synced in Docker instance
sleep 5
docker run  --workdir "/builddir/${DIRNAME}" --volume `pwd`:/builddir:rw "duplicati/debian-build:latest" dpkg-buildpackage

rm -rf "${DIRNAME}"
for filename in "duplicati_${VERSION}-1_amd64.changes" "duplicati_${VERSION}-1.dsc"  "duplicati_${VERSION}-1.tar.gz" 
do
    if [ -f "${filename}" ]; then
        rm "${filename}"
    fi
done
