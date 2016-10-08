#!/bin/bash
# gettext and angular-gettext-cli in PATH necessary
cd $( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )
./duplicati/convert_to_mo.sh
./webroot/compile.sh
