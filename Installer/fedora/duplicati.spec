# TODO:
# - check what thirdparty bins are really needed
# - clean svg file
# - check where Tools/* scripts should really be
# - update l10n (when main code get stabilized)
# - try to fix every mono compiler warning
# - what should I do with Installer/AssemblyRedirects.xml?
# - fix rpmlint warnings

%global debug_package %{nil}

#% global gitdate 20140330
#% global gitcommit 18dba966f35f222a6b4bd054b2431a7abe4651de
#% global gitver HEAD
%global alphatag .git

%global namer duplicati
Name:	%{namer}
Version:	%{_buildversion}
Release:	%{_gittag}%{?alphatag}%{?dist}
Icon: duplicati.xpm
BuildArch:  noarch
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
Source0:	duplicati-%{_builddate}.tar.bz2

# based on libdrm's make-git-snapshot.sh 
# sh duplicati-make-git-snapshot.sh <gitcommit> <_builddate>
Source1:	%{namer}-make-git-snapshot.sh

Patch1:	%{namer}-0001-remove-unittest.patch

BuildRequires:  mono-devel gnome-sharp-devel
BuildRequires:  desktop-file-utils
BuildRequires:  dos2unix

Requires:	desktop-file-utils
Requires:	bash
Requires:	sqlite
Requires:	mono(appindicator-sharp)
Requires:	libappindicator
Requires:	mono(System) >= 4.0
Requires:	mono(System.Configuration)
Requires:	mono(System.Configuration.Install)
Requires:	mono(System.Core)
Requires:	mono(System.Data)
Requires:	mono(System.Drawing)
Requires:	mono(System.Net)
Requires:	mono(System.Net.Http)
Requires:	mono(System.Net.Http.WebRequest)
Requires:	mono(System.Runtime.Serialization)
Requires:	mono(System.ServiceModel)
Requires:	mono(System.ServiceModel.Discovery)
Requires:	mono(System.ServiceProcess)
Requires:	mono(System.Transactions)
Requires:	mono(System.Web)
Requires:	mono(System.Web.Services)
Requires:	mono(System.Xml)
Requires:	mono(System.Xml.Linq)
Requires:	mono(Mono.Security)
Requires:	mono(Mono.Posix)

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
%setup -q -n %{namer}-%{_builddate}
dos2unix Duplicati.sln
dos2unix Tools/Verification/DuplicatiVerify.py
%patch1 -p1

# removing own duplicati binaries:
rm Duplicati/Localization/LocalizationTool.exe
rm -f Duplicati/Localization/Duplicati.Library.Utility.dll

# platform settings:
ln -sf /usr/lib/mono/gtk-sharp-2.0/gtk-sharp.dll \
	./Duplicati/GUI/Duplicati.GUI.TrayIcon/BuildSupport/gtk-sharp.dll

ln -sf /usr/lib/mono/gtk-sharp-2.0/glib-sharp.dll \
	./Duplicati/GUI/Duplicati.GUI.TrayIcon/BuildSupport/glib-sharp.dll

ln -sf /usr/lib/mono/gtk-sharp-2.0/gdk-sharp.dll \
	./Duplicati/GUI/Duplicati.GUI.TrayIcon/BuildSupport/gdk-sharp.dll

ln -sf /usr/lib/mono/gtk-sharp-2.0/atk-sharp.dll \
	./Duplicati/GUI/Duplicati.GUI.TrayIcon/BuildSupport/atk-sharp.dll

# rm thirdparty/appindicator-sharp/appindicator-sharp.dll

find -type f -name "*dll" -or -name "*DLL" -or -name "*exe"

%build

xbuild /property:Configuration=Release BuildTools/UpdateVersionStamp/UpdateVersionStamp.csproj
mono BuildTools/UpdateVersionStamp/bin/Release/UpdateVersionStamp.exe --version=%{_buildversion}

xbuild /property:Configuration=Release thirdparty/UnixSupport/UnixSupport.csproj
cp thirdparty/UnixSupport/bin/Release/UnixSupport.dll thirdparty/UnixSupport/UnixSupport.dll
xbuild /property:Configuration=Release Duplicati.sln

# xbuild BuildTools/LocalizationTool/LocalizationTool.sln

# update l10n

#./make.sh



%install

# remove the app-indicator file, it is supposed to be on the system, if it is supported
# rm Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/appindicator-sharp.dll

# removing non-platform thirdparty binaries:
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/win-tools
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/SQLite/win64
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/SQLite/win32
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/MonoMac.dll
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/alphavss
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/OSX\ Icons
rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/OSXTrayHost

rm -rf Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/licenses/alphavss
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
install -p -m 644 Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/*.dll %{buildroot}%{_exec_prefix}/lib/%{namer}/
install -p -m 755 Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/*.exe %{buildroot}%{_exec_prefix}/lib/%{namer}/
install -p -m 644 Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/*.exe.config %{buildroot}%{_exec_prefix}/lib/%{namer}/
#install -p -m 644 Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/*.dll.config %{buildroot}%{_exec_prefix}/lib/%{namer}/
install -p -m 644 Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/default_compressed_extensions.txt %{buildroot}%{_exec_prefix}/lib/%{namer}/
install -p -m 644 Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/acknowledgements.txt %{buildroot}%{_exec_prefix}/lib/%{namer}/
install -p  Installer/debian/%{namer}.png %{buildroot}%{_datadir}/pixmaps/
install -p -m 644 Duplicati/GUI/Duplicati.GUI.TrayIcon/SVGIcons/dark/* %{buildroot}%{_exec_prefix}/lib/%{namer}/SVGIcons/dark/
install -p -m 644 Duplicati/GUI/Duplicati.GUI.TrayIcon/SVGIcons/light/* %{buildroot}%{_exec_prefix}/lib/%{namer}/SVGIcons/light/
install -p -m 755 Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/lvm-scripts/*.sh %{buildroot}%{_exec_prefix}/lib/%{namer}/lvm-scripts


# Install oem overrides
if [ -f "oem-app-name.txt" ]; then install -p -m 644 "oem-app-name.txt" %{buildroot}%{_exec_prefix}/lib/%{namer}/; fi
if [ -f "oem-update-url.txt" ]; then install -p -m 644 "oem-update-url.txt" %{buildroot}%{_exec_prefix}/lib/%{namer}/; fi
if [ -f "oem-update-key.txt" ]; then install -p -m 644 "oem-update-key.txt" %{buildroot}%{_exec_prefix}/lib/%{namer}/; fi
if [ -f "oem-update-readme.txt" ]; then install -p -m 644 "oem-update-readme.txt" %{buildroot}%{_exec_prefix}/lib/%{namer}/; fi
if [ -f "oem-update-installid.txt" ]; then install -p -m 644 "oem-update-installid.txt" %{buildroot}%{_exec_prefix}/lib/%{namer}/; fi

/bin/bash Installer/fedora/%{namer}-install-recursive.sh "Duplicati/Server/webroot/" "%{buildroot}%{_exec_prefix}/lib/%{namer}/webroot/"

/bin/bash Installer/fedora/%{namer}-install-recursive.sh "Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/licenses/" "%{buildroot}%{_exec_prefix}/lib/%{namer}/licenses/"

desktop-file-install Installer/debian/%{namer}.desktop 

mv Tools/Verification/DuplicatiVerify.py Tools/
rmdir Tools/Verification/
mv Duplicati/Library/Snapshots/lvm-scripts/remove-lvm-snapshot.sh Tools/
mv Duplicati/Library/Snapshots/lvm-scripts/create-lvm-snapshot.sh Tools/
mv Duplicati/Library/Snapshots/lvm-scripts/find-volume.sh Tools/
mv Duplicati/Library/Modules/Builtin/run-script-example.sh Tools/

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
%doc changelog.txt Duplicati/license.txt Tools
%{_bindir}/*
%{_datadir}/*/*
%{_exec_prefix}/lib/*


%changelog
* Sat Apr 23 2016 Kenneth Skovhede <kenneth@duplicati.com> - 2.0.0-0.20160423.git
- Updated list of dependencies

* Thu Mar 27 2014 Kenneth Skovhede <kenneth@duplicati.com> - 2.0.0-0.20140326.git
- Moved to /usr/lib
- Fixed minor build issues

* Wed Mar 26 2014 Kenneth Skovhede <kenneth@duplicati.com> - 2.0.0-0.20140326.git
- Updated patch files
- Fixed minor build issues

* Wed May 29 2013 Ismael Olea <ismael@olea.org> - 2.0.0-0.20130529.git
- removed MacOSX support and deps
- first compiler building spec

* Mon May 13 2013 Ismael Olea <ismael@olea.org> - 1.3.4-1
- removing desktop contents

* Sun May 12 2013 Ismael Olea <ismael@olea.org> - 1.3.4-0
- first dirty package for upstream compiled binary

