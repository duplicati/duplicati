#!/bin/bash
# gettext and angular-gettext-cli in PATH necessary
cd "$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )" || exit 1
./duplicati/convert_to_mo.sh
./webroot/compile.sh
