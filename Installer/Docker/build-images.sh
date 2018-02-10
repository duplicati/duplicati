#!/bin/bash

if [ ! -f "$1" ]; then
	echo "Please provide the filename of an existing zip build as the first argument"
	exit
fi

ARCHITECTURES="amd64 arm32v7"
DEFAULT_ARCHITECTURE=amd64
DEFAULT_CHANNEL=beta
REPOSITORY=duplicati/duplicati

ARCHIVE_NAME=`basename -s .zip $1`
VERSION=`echo "${ARCHIVE_NAME}" | cut -d "-" -f 2-`
CHANNEL=`echo "${ARCHIVE_NAME}" | cut -d "_" -f 2`
DIRNAME=duplicati

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

for arch in ${ARCHITECTURES}; do
	tags="linux-${arch}-${VERSION} linux-${arch}-${CHANNEL}"
	if [ ${CHANNEL} = ${DEFAULT_CHANNEL} ]; then
		tags="linux-${arch} ${tags}"
	fi
	if [ ${arch} = ${DEFAULT_ARCHITECTURE} ]; then
		tags="${VERSION} ${CHANNEL} ${tags}"
	fi
	if [ ${CHANNEL} = ${DEFAULT_CHANNEL} -a ${arch} = ${DEFAULT_ARCHITECTURE} ]; then
		tags="latest ${tags}"
	fi

	args=""
	for tag in ${tags}; do
		args="-t ${REPOSITORY}:${tag} ${args}"
	done

	docker build \
		${args} \
		--build-arg ARCH=${arch}/ \
		--build-arg VERSION=${VERSION} \
		--build-arg CHANNEL=${CHANNEL} \
		--file context/Dockerfile \
		.

	for tag in ${tags}; do
		docker push ${REPOSITORY}:${tag}
	done
done
