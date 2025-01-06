# Create file to prevent file RO image
rm rw.*.template.dmg
touch template.dmg

mkdir -p ./scaffold/Duplicati.app
touch ./scaffold/Duplicati.app/unused.txt

create-dmg \
  --volname Duplicati \
  --volicon volicon.icns \
  --background background.png \
  --text-size 16 \
  --icon-size 120 \
  --icon Duplicati.app 150 120 \
  --app-drop-link 360 120 \
  --disk-image-size 200 \
  template.dmg \
  ./scaffold

# Remove dummy and capture RW image
rm template.dmg
rm -rf scaffold

NAME=(rw.*.template.dmg)
if [[ "$NAME" == "rw.*.template.dmg" ]]; then
    echo "No matching file found"
elif [[ "$NAME" == *' '* ]]; then
    echo "Error: More than one file matched"
else
    mv "$NAME" template.dmg
    bzip2 -z -9 template.dmg
    mv template.dmg.bz2 ../template.dmg.bz2    
fi
