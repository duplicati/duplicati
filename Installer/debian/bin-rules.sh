#!/usr/bin/make -f
# -*- makefile -*-
# Sample debian/rules that uses debhelper.
#
# This file was originally written by Joey Hess and Craig Small.
# As a special exception, when this file is copied by dh-make into a
# dh-make output file, you may use that output file without restriction.
# This special exception was added by Craig Small in version 0.37 of dh-make.
#
# Modified to make a template file for a multi-binary package with separated
# build-arch and build-indep targets  by Bill Allombert 2001

# Uncomment this to turn on verbose mode.
#export DH_VERBOSE=1

# This has to be exported to make some magic below work.
export DH_OPTIONS

%:
	dh $@ 

override_dh_clean:
	dh_clean
	find -type d -name bin | xargs rm -rf
	find -type d -name obj | xargs rm -rf
	find -maxdepth 1 -type d -name build | xargs rm -rf

override_dh_auto_build:
	echo "Not building, using binary package"

override_dh_auto_install:
	mkdir ../temp
	cp -r * ../temp
	mkdir build
	mkdir build/bin
	mkdir build/lib
	mkdir build/lib/duplicati
	mkdir build/lib/duplicati/SQLite
	mkdir build/share
	mkdir build/share/applications
	mkdir build/share/pixmaps
	cp -r ../temp/* build/lib/duplicati
	rm -rf ../temp
	cp ../duplicati-launcher.sh build/bin/duplicati
	cp ../duplicati-commandline-launcher.sh build/bin/duplicati-cli
	cp ../duplicati-server-launcher.sh build/bin/duplicati-server
	cp ../duplicati.desktop build/share/applications
	cp ../duplicati.xpm build/share/pixmaps
	cp ../duplicati.png build/share/pixmaps
	cp ../duplicati.svg build/share/pixmaps
	rm -rf build/lib/duplicati/win-tools
	rm -rf build/lib/duplicati/SQLite/win64
	rm -rf build/lib/duplicati/SQLite/win32
	rm -rf build/lib/duplicati/MonoMac.dll
	rm -rf build/lib/duplicati/alphavss
	rm -rf build/lib/duplicati/OSX\ Icons
	rm -rf build/lib/duplicati/OSXTrayHost
	rm build/lib/duplicati/AlphaFS.dll
	rm build/lib/duplicati/AlphaVSS.Common.dll
	rm -rf build/lib/duplicati/licenses/alphavss
	rm -rf build/lib/duplicati/licenses/MonoMac
	rm -rf build/lib/duplicati/licenses/gpg
	find build/lib/duplicati/* -type f | xargs chmod 644
	find build/lib/duplicati/* -type d | xargs chmod 755
	find build/lib/duplicati/* -type f -name \*.exe | xargs chmod 755
	find build/lib/duplicati/* -type f -name \*.sh | xargs chmod 755
	dh_install
	
override_dh_systemd_enable:
	dh_systemd_enable --no-enable
