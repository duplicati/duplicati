#!/bin/bash

if [ -e ../oem.js ]; then
    echo "Installing OEM script"
    cp ../oem.js build/lib/duplicati/webroot/ngax/scripts/
fi

if [ -e ../oem.css ]; then
    echo "Installing OEM stylesheet"
    cp ../oem.css build/lib/duplicati/webroot/ngax/stylesheets/
fi
