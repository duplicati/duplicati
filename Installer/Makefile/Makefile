BUILD_TOOL=msbuild
BUILD_ARGS=/property:Platform=Any\ CPU /property:Configuration=Release
BUILD_ARGS_DEBUG=/property:Platform=Any\ CPU /property:Configuration=Debug

NUGET_TOOL=nuget
NUGET_ARGS=restore

OUTPUT_ARGS=/property:OutputPath=bin/Release
SOURCE_DIR=../../.
PACKAGE_DIR=./pkg
PKG_TARGET=$(PACKAGE_DIR)/usr/lib/duplicati

all: package

build:
	$(NUGET_TOOL) $(NUGET_ARGS) "$(SOURCE_DIR)/Duplicati.sln"
	$(BUILD_TOOL) $(BUILD_ARGS) "$(SOURCE_DIR)/Duplicati.sln"
	$(BUILD_TOOL) $(BUILD_ARGS) "$(SOURCE_DIR)/BuildTools/UpdateVersionStamp/UpdateVersionStamp.sln"

debug:
	$(NUGET_TOOL) $(NUGET_ARGS) "$(SOURCE_DIR)/Duplicati.sln"
	$(BUILD_TOOL) $(BUILD_ARGS_DEBUG) "$(SOURCE_DIR)/Duplicati.sln"
	$(BUILD_TOOL) $(BUILD_ARGS_DEBUG) "$(SOURCE_DIR)/BuildTools/UpdateVersionStamp/UpdateVersionStamp.sln"

package: build
	if [ -e "$(PACKAGE_DIR)" ]; then \
		rm -rf "$(PACKAGE_DIR)"; \
	fi
	mkdir "$(PACKAGE_DIR)"
	mkdir "$(PACKAGE_DIR)/usr"
	mkdir "$(PACKAGE_DIR)/usr/lib"
	mkdir "$(PACKAGE_DIR)/usr/bin"
	mkdir "$(PACKAGE_DIR)/usr/share"
	mkdir "$(PACKAGE_DIR)/usr/share/applications"
	mkdir "$(PACKAGE_DIR)/usr/share/pixmaps"
	mkdir "$(PACKAGE_DIR)/usr/share/doc"
	mkdir "$(PACKAGE_DIR)/usr/share/doc/duplicati"	
	cp -R "$(SOURCE_DIR)/Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release" "$(PACKAGE_DIR)/usr/lib/duplicati"

	rm -rf "$(PKG_TARGET)/SQLite"
	rm -rf "$(PKG_TARGET)/win-tools"
	rm -rf "$(PKG_TARGET)/"*.mdb

	cp -R "$(SOURCE_DIR)/Duplicati/Server/webroot" "$(PACKAGE_DIR)/usr/lib/duplicati/"
	cp "$(SOURCE_DIR)/Installer/debian/duplicati-launcher.sh" "$(PACKAGE_DIR)/usr/bin/duplicati"
	cp "$(SOURCE_DIR)/Installer/debian/duplicati-commandline-launcher.sh" "$(PACKAGE_DIR)/usr/bin/duplicati-cli"
	cp "$(SOURCE_DIR)/Installer/debian/duplicati-server-launcher.sh" "$(PACKAGE_DIR)/usr/bin/duplicati-server"
	cp "$(SOURCE_DIR)/Installer/debian/duplicati.desktop" "$(PACKAGE_DIR)/usr/share/applications/"
	cp "$(SOURCE_DIR)/Installer/debian/duplicati.xpm" "$(PACKAGE_DIR)/usr/share/pixmaps/"
	cp "$(SOURCE_DIR)/Installer/debian/duplicati.png" "$(PACKAGE_DIR)/usr/share/pixmaps/"

	# If we edit files, we sometimes get backup files included
	find "$(PACKAGE_DIR)" -type f -name \*\~ -exec rm -rf '{}' \;
	
	#Set permissions
	#chown -R root:root "$(PACKAGE_DIR)"
	chmod -R 755 "$(PACKAGE_DIR)"

	find "$(PACKAGE_DIR)" -type d -exec chmod 755 "{}" \;
	find "$(PACKAGE_DIR)" -type f -exec chmod 644 "{}" \;

	for EXT in exe py sh; do \
		find "$(PACKAGE_DIR)" -type f -name \*.$(EXT) -exec chmod 755 "{}" \; ; \
	done
	
	chmod +x "$(PACKAGE_DIR)/usr/bin/duplicati"
	chmod +x "$(PACKAGE_DIR)/usr/bin/duplicati-cli"
	
	if [ -e "duplicati.tar" ]; then \
		rm -rf "duplicati.tar"; \
	fi
	if [ -e "duplicati.tar.gz" ]; then \
		rm -rf "duplicati.tar.gz"; \
	fi
	
	tar -C "$(PACKAGE_DIR)" -cf "duplicati.tar" .
	gzip --best "duplicati.tar"

clean:
	(find "$(SOURCE_DIR)" -type d -name bin -exec rm -rf "{}" \; &> /dev/null) || echo "bin cleaned"
	(find "$(SOURCE_DIR)" -type d -name obj -exec rm -rf "{}" \; &> /dev/null) || echo "obj cleaned"
	(find "$(SOURCE_DIR)/Duplicati/Library/Backend" -name libstorj_uplink.so -exec rm -rf "{}" \;) || echo "storj linux lib cleaned"
	(find "$(SOURCE_DIR)/Duplicati/Library/Backend" -name libstorj_uplink.dylib -exec rm -rf "{}" \;) || echo "storj mac lib cleaned"
	(find "$(SOURCE_DIR)/Duplicati" -type d -name win-x* -exec rm -rf "{}" \;) || echo "storj win lib cleaned"
	rm -rf "$(PACKAGE_DIR)"


.PHONY: build all clean package debug
