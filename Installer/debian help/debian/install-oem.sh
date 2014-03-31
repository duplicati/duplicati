#!/bin/bash

if [ -e ../oem.js ]; then
    cp ../oem.js build/usr/lib/duplicati/webroot/scripts/
fi

if [ -e ../oem.css ]; then
    cp ../oem.css build/usr/lib/duplicati/webroot/stylesheets/
fi
