#!/bin/bash
# gettext in PATH necessary
cd "$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )" || exit 1
for file in *.po; do
    echo 
    msgfmt ${file%.*}.po -o ${file%.*}.mo
done
