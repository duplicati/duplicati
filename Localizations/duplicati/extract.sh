#!/bin/bash
# gettext in PATH necessary
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd $SCRIPT_DIR
cd ../../Duplicati/
find . -type f -name *.cs > $SCRIPT_DIR"/"filelist_cs.txt

# Required to make sure we don't have \r\n in the source files
for file in $(cat $SCRIPT_DIR"/"filelist_cs.txt); do
    tr -d "\r" < "$file" > "$file.tmp"
    mv "$file.tmp" "$file"
done

xgettext -k --from-code=UTF-8 --output=$SCRIPT_DIR"/"localization.pot --files-from=$SCRIPT_DIR"/"filelist_cs.txt --language=C# --keyword=LC.L

# sed -i is broken on OSX
sed -e "s/charset=CHARSET/charset=UTF-8/g" $SCRIPT_DIR"/"localization.pot > $SCRIPT_DIR"/"localization.pot.tmp
mv $SCRIPT_DIR"/"localization.pot.tmp $SCRIPT_DIR"/"localization.pot
rm $SCRIPT_DIR"/"filelist_cs.txt
