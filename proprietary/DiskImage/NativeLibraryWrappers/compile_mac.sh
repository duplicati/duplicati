clang -arch arm64 -dynamiclib -o libSystem_wrapper_arm64.dylib macos_libSystem_wrapper.c
clang -arch x86_64 -dynamiclib -o libSystem_wrapper_x86_64.dylib macos_libSystem_wrapper.c
lipo -create -output libSystem_wrapper.dylib libSystem_wrapper_arm64.dylib libSystem_wrapper_x86_64.dylib
chmod 777 libSystem_wrapper.dylib
rm libSystem_wrapper_arm64.dylib libSystem_wrapper_x86_64.dylib