#!/bin/bash

if [ ! -f "$1" ]; then
	echo "Please provide the filename of an existing zip build as the first argument"
	exit
fi

FILENAME=`basename $1`
DIRNAME=`echo "${FILENAME}" | cut -d "_" -f 1`
VERSION=`echo "${DIRNAME}" | cut -d "-" -f 2`
DATE_STAMP=`LANG=C date -R`
BASE_FILE_NAME="${FILENAME%.*}"

if [ -d "${DIRNAME}" ]; then
	rm -rf "${DIRNAME}"
fi

if [ -f "package.tgz" ]; then
    rm -rf "package.tgz"
fi

if [ -f "${BASE_FILE_NAME}.spk" ]; then
    rm -rf "${BASE_FILE_NAME}.spk"
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

cd "${DIRNAME}"

tar cvf ../package.tgz ./*
cd ..

rm -rf "${DIRNAME}"

git checkout INFO
echo "version=\"${VERSION}\"" >> INFO

tar cf "${BASE_FILE_NAME}.spk" INFO LICENSE *.PNG package.tgz scripts conf WIZARD_UIFILES
git reset INFO

rm package.tgz