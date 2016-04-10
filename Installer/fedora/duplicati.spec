# TODO:
# - check what thirdparty bins are really needed
# - clean svg file
# - check where Tools/* scripts should really be
# - update l10n (when main code get stabilized)
# - try to fix every mono compiler warning
# - what should I do with Installer/AssemblyRedirects.xml?
# - fix rpmlint warnings

%global debug_package %{nil}

%global gitdate 20140330
#%global gitcommit 18dba966f35f222a6b4bd054b2431a7abe4651de
#%global gitver HEAD
%global alphatag git

%global namer duplicati
Name:	%{namer}2
Version:	2.0.0
Release:	0.%{gitdate}%{?alphatag}%{?dist}
BuildArch:  noarch

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
BuildRequires:  dos2unix

Requires:	desktop-file-utils
Requires:	bash
Requires:	mono(System), mono(System.Web)
Requires:	sqlite
Requires:   mono(appindicator-sharp)
Requires:   libappindicator

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
dos2unix Duplicati/CommandLine/Duplicati.CommandLine.csproj
dos2unix Duplicati/Library/Utility/Duplicati.Library.Utility.csproj
dos2unix Duplicati/Library/Snapshots/Duplicati.Library.Snapshots.csproj
dos2unix Duplicati/GUI/Duplicati.GUI.TrayIcon/Duplicati.GUI.TrayIcon.csproj
dos2unix Duplicati/GUI/Duplicati.GUI.TrayIcon/Program.cs
dos2unix Duplicati/License/Duplicati.License.csproj
dos2unix Duplicati.sln
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

rm -rf thirdparty/alphavss/platform
rm thirdparty/Signer/Signer.exe

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

xbuild /property:Configuration=Release Duplicati.sln
# xbuild BuildTools/LocalizationTool/LocalizationTool.sln

# update l10n

#./make.sh



%install

# Mono binaries are installed in /usr/lib, not /usr/lib64, even on x86_64:
# https://fedoraproject.org/wiki/Packaging:Mono

install -d %{buildroot}%{_datadir}/pixmaps/
install -d %{buildroot}%{_exec_prefix}/lib/%{namer}/
install -d %{buildroot}%{_exec_prefix}/lib/%{namer}/SVGIcons/
install -d %{buildroot}%{_exec_prefix}/lib/%{namer}/SVGIcons/dark/
install -d %{buildroot}%{_exec_prefix}/lib/%{namer}/SVGIcons/light/
install -d %{buildroot}%{_exec_prefix}/lib/%{namer}/licenses
install -p -D -m 755 Installer/debian/duplicati-launcher.sh %{buildroot}%{_bindir}/%{namer}
install -p -D -m 755 Installer/debian/duplicati-commandline-launcher.sh %{buildroot}%{_bindir}/%{namer}-cli
install -p -D -m 755 Installer/debian/duplicati-server-launcher.sh %{buildroot}%{_bindir}/%{namer}-server
install -p -m 755 Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/*.dll %{buildroot}%{_exec_prefix}/lib/%{namer}/
install -p -m 755 Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/*.exe %{buildroot}%{_exec_prefix}/lib/%{namer}/
install -p -m 755 Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/*.exe.config %{buildroot}%{_exec_prefix}/lib/%{namer}/
#install -p -m 755 Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/*.dll.config %{buildroot}%{_exec_prefix}/lib/%{namer}/
install -p -m 755 Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/default_compressed_extensions.txt %{buildroot}%{_exec_prefix}/lib/%{namer}/
install -p  Installer/debian/%{namer}.png %{buildroot}%{_datadir}/pixmaps/
install -p -m 755 Duplicati/GUI/Duplicati.GUI.TrayIcon/SVGIcons/dark/* %{buildroot}%{_exec_prefix}/lib/%{namer}/SVGIcons/dark/
install -p -m 755 Duplicati/GUI/Duplicati.GUI.TrayIcon/SVGIcons/light/* %{buildroot}%{_exec_prefix}/lib/%{namer}/SVGIcons/light/

cp -r Duplicati/Server/webroot %{buildroot}%{_exec_prefix}/lib/%{namer}/webroot
chmod -R 655 %{buildroot}%{_exec_prefix}/lib/%{namer}/webroot
cp -r Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/licenses %{buildroot}%{_exec_prefix}/lib/%{namer}/licenses
chmod -R 655 %{buildroot}%{_exec_prefix}/lib/%{namer}/licenses

desktop-file-install Installer/debian/%{namer}.desktop 

# thirdparty dependencies

find thirdparty/ -type f -\( -name "*DLL" -or -name "*dll" -\) \
	-exec install -p -m 755 {} %{buildroot}%{_exec_prefix}/lib/%{namer}/ \;

mv Tools/Verification/DuplicatiVerify.py Tools/
rmdir Tools/Verification/
mv Duplicati/Library/Snapshots/lvm-scripts/remove-lvm-snapshot.sh Tools/
mv Duplicati/Library/Snapshots/lvm-scripts/create-lvm-snapshot.sh Tools/
mv Duplicati/Library/Snapshots/lvm-scripts/find-volume.sh Tools/
mv Duplicati/Library/Modules/Builtin/run-script-example.sh Tools/
mv Installer/linux\ help/linux-readme.txt .

# remove the app-indicator file, it is supposed to be on the system, if it is supported
rm %{buildroot}%{_exec_prefix}/lib/%{namer}/appindicator-sharp.dll

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
%doc releasenotes.txt changelog.txt Duplicati/license.txt Tools linux-readme.txt
%{_bindir}/*
%{_datadir}/*/*
%{_exec_prefix}/lib/*


%changelog
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

