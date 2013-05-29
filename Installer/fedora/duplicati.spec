# TODO:
# - check what thirdparty bins are really needed
# - clean svg file
# - check where Tools/* scripts should really be
# - update l10n (when main code get stabilized)
# - try to fix every mono compiler warning
# - what should I do with Installer/AssemblyRedirects.xml?
# - fix rpmlint warnings

%global debug_package %{nil}

%global gitdate 20130529
#%global gitcommit 18dba966f35f222a6b4bd054b2431a7abe4651de
#%global gitver HEAD
%global alphatag git

%global namer duplicati
Name:	%{namer}2
Version:	2.0.0
Release:	0.%{gitdate}%{?alphatag}%{?dist}

Summary:	Backup client for encrypted online backups
License:	LGPLv2+
URL:	http://www.duplicati.com
#Source0:	http://duplicati.googlecode.com/files/Duplicati%20%{version}.tgz
Source0:	duplicati-%{gitdate}.tar.bz2

# based on libdrm's make-git-snapshot.sh 
# sh duplicati-make-git-snapshot.sh <gitcommit> <gitdate>
Source1:	%{namer}-make-git-snapshot.sh

Patch2:	%{namer}-0002-fedora-clean-build.patch
Patch3:	%{namer}-0003-remove-monomac.patch

BuildRequires:  mono-devel gnome-sharp-devel
BuildRequires:  desktop-file-utils

Requires:	desktop-file-utils
Requires:	bash
Requires:	mono(System), mono(System.Web), mono(System.Windows.Forms)
Requires:	sqlite

Conflicts:	duplicati < 2.0.0

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
%setup -q -n %{namer}-%{gitdate}
#%patch0 -p1
%patch2 -p1
%patch3 -p1

# removing own duplicati binaries:
rm Duplicati/Localization/LocalizationTool.exe
rm -f Duplicati/Localization/Duplicati.Library.Utility.dll
rm UpdateVersionNumber.exe
rm Installer/WixProjBuilder.exe
rm Installer/WixIncludeMake.exe

# removing non-platform thirdparty binaries:
rm thirdparty/SQLite/Bin/sqlite3.dll
rm thirdparty/SQLite/win32/System.Data.SQLite.dll
rm thirdparty/SQLite/win64/System.Data.SQLite.dll
rm thirdparty/MonoMac/MonoMac.dll
rm thirdparty/SQLite/Bin/sqlite-3.6.12.so
rm thirdparty/gpg/gpg2.exe
rm thirdparty/gpg/gpg/gpg.exe
rm thirdparty/gpg/iconv.dll
rm thirdparty/gpg/libadns-1.dll
rm thirdparty/gpg/libassuan-0.dll
rm thirdparty/gpg/libgcrypt-11.dll
rm thirdparty/gpg/libgpg-error-0.dll
rm thirdparty/gpg/zlib1.dll

# dunno if they are crossplatform or *ix especific:
#rm thirdparty/alphavss/Bin/AlphaFS.dll
#rm thirdparty/alphavss/Bin/AlphaVSS.Common.dll
#thirdparty/UnixSupport/UnixSupport.dll

rm thirdparty/alphavss/platform/AlphaVSS.WinXP.x86.dll
rm thirdparty/alphavss/platform/AlphaVSS.WinXP.x64.dll
rm thirdparty/alphavss/platform/AlphaVSS.Win2008.x86.dll
rm thirdparty/alphavss/platform/AlphaVSS.Win2008.x64.dll
rm thirdparty/alphavss/platform/AlphaVSS.Win2003.x86.dll
rm thirdparty/alphavss/platform/AlphaVSS.Win2003.x64.dll
rm thirdparty/TaskScheduler/Microsoft.Win32.TaskScheduler.dll
rm thirdparty/Signer/Signer.exe
rm thirdparty/Putty/psftp.exe
rm thirdparty/Putty/pscp.exe
rm thirdparty/LightDataModel/DataClassFileBuilder.exe

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

# repasar qué hacemos con esto:
#xbuild ./thirdparty/ObjectListView/ObjectListView2010.sln 

xbuild /property:Configuration=Release Duplicati.sln
#xbuild /property:Configuration=Release Duplicati\ Scheduler.sln

xbuild BuildTools/LocalizationTool/LocalizationTool.sln

# update l10n

#./make.sh

%install

install -d %{buildroot}%{_libdir}/%{namer}/
install -d %{buildroot}%{_datadir}/pixmaps/
install -p -D -m 755 Installer/debian\ help/duplicati-launcher.sh %{buildroot}%{_bindir}/%{namer}
install -p -D -m 755 Installer/debian\ help/duplicati-commandline-launcher.sh %{buildroot}%{_bindir}/%{namer}-cli
install -p  -m 755 Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/Duplicati.* %{buildroot}%{_libdir}/%{namer}/
install -p  Installer/debian\ help/%{namer}.png %{buildroot}%{_datadir}/pixmaps/

desktop-file-install Installer/debian\ help/%{namer}.desktop 

# thirdparty dependencies

find thirdparty/ -type f -\( -name "*DLL" -or -name "*dll" -\) \
	-exec install -p -m 755 {} %{buildroot}%{_libdir}/%{namer}/ \;

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
%doc releasenotes.txt changelog.txt Duplicati/license.txt Tools Installer/linux\ help/linux-readme.txt
%{_bindir}/*
%{_datadir}/*/*
%{_libdir}/*


%changelog
* Wed May 29 2013 Ismael Olea <ismael@olea.org> - 2.0.0-0.20130529.git
- removed MacOSX support and deps
- first compiler building spec

* Mon May 13 2013 Ismael Olea <ismael@olea.org> - 1.3.4-1
- removing desktop contents

* Sun May 12 2013 Ismael Olea <ismael@olea.org> - 1.3.4-0
- first dirty package for upstream compiled binary

