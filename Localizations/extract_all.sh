#!/bin/bash
# gettext and angular-gettext-cli in PATH necessary
cd $( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )
./duplicati/extract.sh
./webroot/extract.sh
