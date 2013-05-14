# Non debug info in precompiled package
%global debug_package %{nil}


Name:	duplicati
Version:	1.3.4
Release:	1%{dist}
Summary:	Backup client for encrypted online backups
License:	LGPLv2+
URL:	http://www.duplicati.com
#Source0:	http://duplicati.googlecode.com/files/Duplicati%20%{version}.tgz
Source0:	Duplicati %{version}.tgz

Requires:	desktop-file-utils
Requires:	bash
Requires:	mono(System), mono(System.Web), mono(System.Windows.Forms)

# we don't want automatic dependencies generation because
# precompiled binaries generates weird ones:
%global __requires_exclude ^mono.*$


%description 
Duplicati is a free backup client that securely stores encrypted,
incremental, compressed backups on cloud storage services and remote file
servers.  It supports targets like Amazon S3, Windows Live SkyDrive,
Rackspace Cloud Files or WebDAV, SSH, FTP (and many more).
 
Duplicati has built-in AES-256 encryption and backups be can signed using
GNU Privacy Guard.  A built-in scheduler makes sure that backups are always
up-to-date.  Last but not least, Duplicati provides various options and
tweaks like filters, deletion rules, transfer and bandwidth options to run
backups for specific purposes.

%prep
%setup -q -c -n %{name}-%{version}-bin


%build
# binary package, nothing to build


%install
rm -rf install/
rm -rf usr/share/pixmaps/duplicati.xpm

#for files/doc declaration:
mv usr/share/doc/duplicati/README .
rm usr/share/doc/duplicati/changelog.Debian.gz 
mv usr/share/doc/duplicati/copyright .
mv usr/share/doc/duplicati/changelog.gz .
rmdir usr/share/doc/duplicati/ usr/share/doc/
mv usr/ %{buildroot}

desktop-file-install %{buildroot}%{_datadir}/applications/%{name}.desktop 


%post
/bin/touch --no-create %{_datadir}/icons/hicolor || :
%{_bindir}/gtk-update-icon-cache \
  --quiet %{_datadir}/icons/hicolor 2> /dev/null|| :

%postun
/bin/touch --no-create %{_datadir}/icons/hicolor || :
%{_bindir}/gtk-update-icon-cache \
  --quiet %{_datadir}/icons/hicolor 2> /dev/null|| :

%posttrans
/usr/bin/gtk-update-icon-cache %{_datadir}/icons/hicolor &>/dev/null || :


%files
%doc README copyright changelog.gz 
%{_bindir}/*
%{_datadir}/*
%{_libdir}/*


%changelog
* Mon May 13 2013 Ismael Olea <ismael@olea.org> - 1.3.4-1
- removing desktop contents

* Sun May 12 2013 Ismael Olea <ismael@olea.org> - 1.3.4-0
- first dirty package for upstream compiled binary

