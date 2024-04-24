#!/bin/bash

# This script is a workaround for Docker desktop not handling
# permissions for mounted volumes correctly. 

# This script is intended to be run inside a Docker container

mkdir /build-temp
cp -R $1/* /build-temp

rpmbuild -bb --target $2 --define "_topdir /build-temp" SOURCES/duplicati.spec
mv /build-temp/RPMS/$2/*.rpm /$1/build.rpm