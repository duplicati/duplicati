BUILD_TOOL=xbuild
BUILD_ARGS=/property:Platform=Any\ CPU /property:Configuration=Release
OUTPUT_ARGS=/property:OutputPath=bin/Release
SOURCE_DIR=../../.

all: build

Duplicati-v1.sln:
	patch -d $(SOURCE_DIR) -i patch-1-remove-scheduler -o Duplicati-v1.sln Duplicati.sln

Duplicati-v2.sln: Duplicati-v1.sln
	patch -d $(SOURCE_DIR) -i patch-2-remove-unsigned-backends -o Duplicati-v2.sln Duplicati-v1.sln

build: Duplicati-v2.sln
	$(BUILD_TOOL) $(BUILD_ARGS) $(SOURCE_DIR)/Duplicati-v2.sln
	$(BUILD_TOOL) $(BUILD_ARGS) $(OUTPUT_ARGS) $(SOURCE_DIR)/Duplicati/Library/Backend/SSH/Duplicati.Library.Backend.SSH.csproj
	$(BUILD_TOOL) $(BUILD_ARGS) $(OUTPUT_ARGS) $(SOURCE_DIR)/Duplicati/Library/Backend/TahoeLAFS/Duplicati.Library.Backend.TahoeLAFS.csproj

clean:
	rm -rf $(SOURCE_DIR)/Duplicati-v1.sln
	rm -rf $(SOURCE_DIR)/Duplicati-v2.sln
	(find $(SOURCE_DIR) -type d -name bin -exec rm -rf "{}" \; &> /dev/null) || echo "bin cleaned"
	(find $(SOURCE_DIR) -type d -name obj -exec rm -rf "{}" \; &> /dev/null) || echo "obj cleaned"



.PHONY: build all clean install