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
Release:	%{_buildtag}
Icon: duplicati.xpm
BuildArch:  noarch

# Disable auto dependencies as it picks up .Net 2.0 profile
#   and does not support supplying them with 4.5
# Also, all thirdparty libraries are given as "provides" but they
#   are not installed for use externally
AutoReqProv: no

Summary:	Backup client for encrypted online backups
License:	LGPLv2+
URL:	http://www.duplicati.com
Source0:	duplicati-%{_buildversion}.tar.bz2
Source1:	%{namer}-make-binary-package.sh
Source2: 	%{namer}-install-recursive.sh
Source3: 	%{namer}.service
Source4: 	%{namer}.default

BuildRequires:  desktop-file-utils
BuildRequires:  dos2unix
BuildRequires:  systemd

Requires:	desktop-file-utils
Requires:	bash
Requires:	sqlite >= 3.6.12
Requires:	mono(appindicator-sharp)
Requires:	libappindicator
Requires:	mono-core >= 3.0
Requires:	mono-data-sqlite
Requires:	mono(System)
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
%setup -q -n %{namer}-%{_buildversion}

%build

# removing non-platform thirdparty binaries:
rm -rf win-tools
rm -rf SQLite/win64
rm -rf SQLite/win32
rm -rf MonoMac.dll
rm -rf alphavss
rm -rf OSX\ Icons
rm -rf OSXTrayHost
rm AlphaFS.dll
rm AlphaVSS.Common.dll
rm -rf licenses/alphavss
rm -rf licenses/MonoMac
rm -rf licenses/gpg


%install

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

/bin/bash %{_topdir}/SOURCES/%{namer}-install-recursive.sh "." "%{buildroot}%{_exec_prefix}/lib/%{namer}/"

# We do not want these files in the lib folder
rm "%{buildroot}%{_exec_prefix}/lib/%{namer}/%{namer}-launcher.sh"
rm "%{buildroot}%{_exec_prefix}/lib/%{namer}/%{namer}-commandline-launcher.sh"
rm "%{buildroot}%{_exec_prefix}/lib/%{namer}/%{namer}-server-launcher.sh"
rm "%{buildroot}%{_exec_prefix}/lib/%{namer}/%{namer}.png"
rm "%{buildroot}%{_exec_prefix}/lib/%{namer}/%{namer}.desktop"

# Then we install them in the correct spots
install -p -D -m 755 %{namer}-launcher.sh %{buildroot}%{_bindir}/%{namer}
install -p -D -m 755 %{namer}-commandline-launcher.sh %{buildroot}%{_bindir}/%{namer}-cli
install -p -D -m 755 %{namer}-server-launcher.sh %{buildroot}%{_bindir}/%{namer}-server
install -p  %{namer}.png %{buildroot}%{_datadir}/pixmaps/

# And fix permissions
find "%{buildroot}%{_exec_prefix}/lib/%{namer}"/* -type f -name \*.exe | xargs chmod 755
find "%{buildroot}%{_exec_prefix}/lib/%{namer}"/* -type f -name \*.sh | xargs chmod 755
#find "%{buildroot}%{_exec_prefix}/lib/%{namer}"/* -type f -name \*.py | xargs chmod 755

desktop-file-install %{namer}.desktop

# Install the service:
install -p -D -m 755 %{_topdir}/SOURCES/%{namer}.service %{_unitdir}
install -p -D -m 644 %{_topdir}/SOURCES/%{namer}.default %{_sysconfdir}/sysconfig/

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
%doc changelog.txt licenses/license.txt
%{_bindir}/*
%{_datadir}/*/*
%{_exec_prefix}/lib/*


%changelog
* Wed Jun 21 2017 Kenneth Skovhede <kenneth@duplicati.com> - 2.0.0-0.20170621.git
- Added the service file to the install

* Thu Apr 28 2016 Kenneth Skovhede <kenneth@duplicati.com> - 2.0.0-0.20160423.git
- Made a binary version of the spec file

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


