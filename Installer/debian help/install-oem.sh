#!/bin/bash

if [ -e ../oem.js ]; then
    echo "Installing OEM script"
    cp ../oem.js build/usr/lib/duplicati/webroot/scripts/
fi

if [ -e ../oem.css ]; then
    echo "Installing OEM stylesheet"
    cp ../oem.css build/usr/lib/duplicati/webroot/stylesheets/
fi
