mkdir -p ~/rpmbuild/SOURCES/



cp /buildroot/duplicati.xpm ~/rpmbuild/SOURCES/
# cp make-binary-package.sh ~/rpmbuild/SOURCES/duplicati-make-binary-package.sh
# cp duplicati-install-recursive.sh ~/rpmbuild/SOURCES/duplicati-install-recursive.sh
# cp duplicati.service ~/rpmbuild/SOURCES/duplicati.service
# cp duplicati.default ~/rpmbuild/SOURCES/duplicati.default


BUILDDATE=$(LANG=C date -R)
GITTAG="1"

echo Creating Tar...
(cd /sources/ && tar --exclude="./.git" --transform "s,^./,duplicati-${VERSION}/," -cjf ~/rpmbuild/SOURCES/duplicati-${VERSION}.tar.bz2 .)
tar -tf ~/rpmbuild/SOURCES/duplicati-${VERSION}.tar.bz2 | head
echo Done...

echo "%global _builddate ${BUILDDATE}" >> ~/rpmbuild/SOURCES/duplicati-buildinfo.spec
echo "%global _buildversion ${VERSION}" >> ~/rpmbuild/SOURCES/duplicati-buildinfo.spec
echo "%global _gittag ${GITTAG}" >> ~/rpmbuild/SOURCES/duplicati-buildinfo.spec

cd /buildroot
dos2unix duplicati.spec
rpmbuild -bb duplicati.spec

cp /root/rpmbuild/RPMS/*/*.rpm /sources/