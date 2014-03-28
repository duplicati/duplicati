#!/bin/bash

DATE=`date +%Y%m%d`

git pull
bash duplicati-make-git-snapshot.sh
mv duplicati-$DATE.tar.bz2 ~/rpmbuild/SOURCES/ 
cp *.sh ~/rpmbuild/SOURCES/
cp *.patch ~/rpmbuild/SOURCES/
if [ -e ./oem.js ]; then
    cp ./oem.js ~/rpmbuild/SOURCES/
fi

if [ -e ./oem.css ]; then
    cp ./oem.css ~/rpmbuild/SOURCES/
fi

rpmbuild -bs duplicati.spec
rpmbuild -bb duplicati.spec