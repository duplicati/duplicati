cd /deb

DATE_STAMP=$(LANG=C date -R)
sed -e "s;%VERSION%;$VERSION;g" -e "s;%DATE%;$DATE_STAMP;g" -i "debian/changelog"

cat debian/changelog

cp /sources/changelog.txt ./

dpkg-buildpackage -b --no-sign

cp /*.deb /sources/