#!/bin/bash
# gettext in PATH necessary
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd $SCRIPT_DIR
cd ../../Duplicati/
find . -type f -name *.cs > $SCRIPT_DIR"/"filelist_cs.txt
xgettext -k --from-code=UTF-8 --output=$SCRIPT_DIR"/"localization.pot --files-from=$SCRIPT_DIR"/"filelist_cs.txt --language=C# --keyword=LC.L
sed -i "s/charset=CHARSET/charset=UTF-8/g" $SCRIPT_DIR"/"localization.pot
rm $SCRIPT_DIR"/"filelist_cs.txt
