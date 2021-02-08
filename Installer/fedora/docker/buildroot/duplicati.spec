# TODO:
# - check where Tools/* scripts should really be
# - try to fix every mono compiler warning
# - fix rpmlint warnings

# Set up some defaults
%global namer duplicati
%global debug_package %{nil}
%global alphatag .git

# Then load overrides
%include %{_topdir}/SOURCES/%{namer}-buildinfo.spec

Name:	%{namer}
Version:	%{_buildversion}
Release:	%{_gittag}%{?alphatag}%{?dist}
Icon: duplicati.xpm
#BuildArch:  noarch
#Should work, but does not allow building noarch
#ExclusiveArch: % {mono_arches}

# Disable auto dependencies as it picks up .Net 2.0 profile
#   and does not support supplying them with 4.5
# Also, all thirdparty libraries are given as "provides" but they
#   are not installed for use externally
AutoReqProv: no

Summary:	Backup client for encrypted online backups
License:	LGPLv2+
URL:	http://www.duplicati.com
#Source0:	http://duplicati.googlecode.com/files/Duplicati%20% {_buildversion}.tgz
Source0:	duplicati-%{_buildversion}.tar.bz2

# based on libdrm's make-git-snapshot.sh 
# sh duplicati-make-git-snapshot.sh <gitcommit> <_builddate>
Source1:	%{namer}-build-package.sh
Source2:	%{namer}-make-git-snapshot.sh
Source3:	%{namer}-buildinfo.spec

BuildRequires:  desktop-file-utils
BuildRequires:  dos2unix
BuildRequires:  systemd

Requires:	desktop-file-utils
Requires:	bash
Requires:	libappindicator

Provides:	duplicati
Provides:	duplicati-cli
Provides:	duplicati-server

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
%setup -q

find -type f -name "*dll" -or -name "*DLL" -or -name "*exe"

%build

dotnet publish -c Release --runtime=linux-x64 -o publish Duplicati.sln

%install

# removing non-platform thirdparty binaries:
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/win-tools
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/SQLite/win64
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/SQLite/win32
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/MonoMac.dll
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/OSX\ Icons
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/OSXTrayHost

rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/licenses/MonoMac
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/licenses/gpg

# Mono binaries are installed in /usr/lib, not /usr/lib64, even on x86_64:
# https://fedoraproject.org/wiki/Packaging:Mono

install -d %{buildroot}%{_datadir}/pixmaps/
install -d %{buildroot}%{_exec_prefix}/lib/%{namer}/
install -d %{buildroot}%{_exec_prefix}/lib/%{namer}/SVGIcons/
install -d %{buildroot}%{_exec_prefix}/lib/%{namer}/SVGIcons/dark/
install -d %{buildroot}%{_exec_prefix}/lib/%{namer}/SVGIcons/light/
install -d %{buildroot}%{_exec_prefix}/lib/%{namer}/licenses/
install -d %{buildroot}%{_exec_prefix}/lib/%{namer}/webroot/
install -d %{buildroot}%{_exec_prefix}/lib/%{namer}/lvm-scripts/

install -p -D -m 755 Installer/debian/duplicati-launcher.sh %{buildroot}%{_bindir}/%{namer}
install -p -D -m 755 Installer/debian/duplicati-commandline-launcher.sh %{buildroot}%{_bindir}/%{namer}-cli
install -p -D -m 755 Installer/debian/duplicati-server-launcher.sh %{buildroot}%{_bindir}/%{namer}-server

# Install oem overrides
if [ -f "oem-app-name.txt" ]; then install -p -m 644 "oem-app-name.txt" %{buildroot}%{_exec_prefix}/lib/%{namer}/; fi
if [ -f "oem-update-url.txt" ]; then install -p -m 644 "oem-update-url.txt" %{buildroot}%{_exec_prefix}/lib/%{namer}/; fi
if [ -f "oem-update-key.txt" ]; then install -p -m 644 "oem-update-key.txt" %{buildroot}%{_exec_prefix}/lib/%{namer}/; fi
if [ -f "oem-update-readme.txt" ]; then install -p -m 644 "oem-update-readme.txt" %{buildroot}%{_exec_prefix}/lib/%{namer}/; fi
if [ -f "oem-update-installid.txt" ]; then install -p -m 644 "oem-update-installid.txt" %{buildroot}%{_exec_prefix}/lib/%{namer}/; fi

/bin/bash Installer/fedora/%{namer}-install-recursive.sh "publish/" "%{buildroot}%{_exec_prefix}/lib/%{namer}/"

desktop-file-install Installer/debian/%{namer}.desktop 

mv Duplicati/Library/Snapshots/lvm-scripts/remove-lvm-snapshot.sh Tools/
mv Duplicati/Library/Snapshots/lvm-scripts/create-lvm-snapshot.sh Tools/
mv Duplicati/Library/Snapshots/lvm-scripts/find-volume.sh Tools/
mv Duplicati/Library/Modules/Builtin/run-script-example.sh Tools/

# Install the service:
install -p -D -m 755 Installer/fedora/%{namer}.service %{_unitdir}
install -p -D -m 644 Installer/fedora/%{namer}.default %{_sysconfdir}/sysconfig/


%post
/bin/touch --no-create %{_datadir}/icons/hicolor || :
%{_bindir}/gtk-update-icon-cache \
  --quiet %{_datadir}/icons/hicolor 2> /dev/null|| :
%systemd_post %{namer}.service

%preun
%systemd_preun %{namer}.service

%postun
/bin/touch --no-create %{_datadir}/icons/hicolor || :
%{_bindir}/gtk-update-icon-cache \
  --quiet %{_datadir}/icons/hicolor 2> /dev/null|| :
%systemd_postun_with_restart %{namer}.service

%posttrans
/usr/bin/gtk-update-icon-cache %{_datadir}/icons/hicolor &>/dev/null || :


%files
%doc changelog.txt Duplicati/license.txt Tools
%{_bindir}/*
%{_datadir}/*/*
%{_exec_prefix}/lib/*


%changelog
* Fri Jan 01 2021 Kenneth Skovhede 2.0
- Packaged release
- See changelog.txt for changes

