#!/bin/bash
# gettext in PATH necessary
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd $SCRIPT_DIR
cd ../../Duplicati/
find . -type f -name *.cs > $SCRIPT_DIR"/"filelist_cs.txt

# Required to make sure we don't have \r\n in the extracted strings.
# Strip them in a temporary copy, so the source files keep their line endings
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT
while IFS= read -r file; do
    mkdir -p "$TMP_DIR/$(dirname "$file")"
    tr -d "\r" < "$file" > "$TMP_DIR/$file"
done < $SCRIPT_DIR"/"filelist_cs.txt
cd "$TMP_DIR"

xgettext -k --from-code=UTF-8 --output=$SCRIPT_DIR"/"localization.pot --files-from=$SCRIPT_DIR"/"filelist_cs.txt --language=C# --keyword=LC.L

# sed -i is broken on OSX
sed -e "s/charset=CHARSET/charset=UTF-8/g" $SCRIPT_DIR"/"localization.pot > $SCRIPT_DIR"/"localization.pot.tmp
mv $SCRIPT_DIR"/"localization.pot.tmp $SCRIPT_DIR"/"localization.pot
rm $SCRIPT_DIR"/"filelist_cs.txt
