#!/bin/bash
# angular-gettext-cli in PATH necessary
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd $SCRIPT_DIR
cd ../../Duplicati/Server/webroot/ngax
angular-gettext-cli --files "**/*.+(js|html)" --dest $SCRIPT_DIR"/"localization_webroot.pot
