BUILD_TOOL=xbuild
BUILD_ARGS=/property:Platform=Any\ CPU /property:Configuration=Release
OUTPUT_ARGS=/property:OutputPath=bin/Release
SOURCE_DIR=../../.
LOCALIZATION_TOOL=mono $(SOURCE_DIR)/Duplicati/Localization/LocalizationTool.exe

all: build translations

Duplicati-v1.sln:
	patch -d "$(SOURCE_DIR)" -i patch-1-remove-scheduler -o Duplicati-v1.sln Duplicati.sln

Duplicati-v2.sln: Duplicati-v1.sln
	patch -d "$(SOURCE_DIR)" -i patch-2-remove-unsigned-backends -o Duplicati-v2.sln Duplicati-v1.sln

build: Duplicati-v2.sln
	$(BUILD_TOOL) $(BUILD_ARGS) "$(SOURCE_DIR)/Duplicati-v2.sln"
	$(BUILD_TOOL) $(BUILD_ARGS) $(OUTPUT_ARGS) "$(SOURCE_DIR)/Duplicati/Library/Backend/SSH/Duplicati.Library.Backend.SSH.csproj"
	$(BUILD_TOOL) $(BUILD_ARGS) $(OUTPUT_ARGS) "$(SOURCE_DIR)/Duplicati/Library/Backend/TahoeLAFS/Duplicati.Library.Backend.TahoeLAFS.csproj"
	cp "$(SOURCE_DIR)/Duplicati/Library/Backend/SSH/bin/Release/"*.dll "$(SOURCE_DIR)/Duplicati/GUI/bin/Release/"
	cp "$(SOURCE_DIR)/Duplicati/Library/Backend/TahoeLAFS/bin/Release/"*.dll "$(SOURCE_DIR)/Duplicati/GUI/bin/Release/"
	$(BUILD_TOOL) $(BUILD_ARGS) "$(SOURCE_DIR)/BuildTools/LocalizationTool/LocalizationTool.sln"

	rm -rf "$(SOURCE_DIR)/Duplicati/GUI/bin/Release/alphavss"
	rm -rf "$(SOURCE_DIR)/Duplicati/GUI/bin/Release/SQLite"
	rm -rf "$(SOURCE_DIR)/Duplicati/GUI/bin/Release/win-tools"
	rm -rf "$(SOURCE_DIR)/Duplicati/GUI/bin/Release/"*.mdb
	rm -rf "$(SOURCE_DIR)/Duplicati/GUI/bin/Release/AlphaFS.dll"
	rm -rf "$(SOURCE_DIR)/Duplicati/GUI/bin/Release/AlphaVSS.Common.dll"

translations: build
	$(BUILD_TOOL) $(BUILD_ARGS) "$(SOURCE_DIR)/BuildTools/LocalizationTool/LocalizationTool.sln"
	for lang_file in `find "$(SOURCE_DIR)/Duplicati/Localization" -type f -name report.\*.csv`; do \
		ccode=`basename "$$lang_file"`; \
		ccode=$${ccode%%.csv}; \
		ccode=$${ccode##report.}; \
		$(LOCALIZATION_TOOL) import $$ccode "$(SOURCE_DIR)/Duplicati/Localization/$$lang_file"; \
	done
	$(LOCALIZATION_TOOL) build

clean:
	rm -rf "$(SOURCE_DIR)/Duplicati-v1.sln"
	rm -rf "$(SOURCE_DIR)/Duplicati-v2.sln"
	(find "$(SOURCE_DIR)" -type d -name bin -exec rm -rf "{}" \; &> /dev/null) || echo "bin cleaned"
	(find "$(SOURCE_DIR)" -type d -name obj -exec rm -rf "{}" \; &> /dev/null) || echo "obj cleaned"
	(find "$(SOURCE_DIR)/Duplicati/Localization/" -type d -not -name Localization -exec rm -rf "{}" \; &> /dev/null) || echo "localization cleaned"



.PHONY: build all clean install translations