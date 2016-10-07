#!/bin/bash
# angular-gettext-cli in PATH necessary
cd $( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )
angular-gettext-cli --compile --files "*.po" --dest ../../Duplicati/Server/webroot/ngax/scripts/angular-gettext-cli_compiled_js_output.js --format javascript --module backupApp
