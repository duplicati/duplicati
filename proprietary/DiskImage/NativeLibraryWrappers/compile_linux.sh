#!/bin/bash
set -e

# Compile Linux libc wrapper for multiple architectures
# Note: Unlike macOS, Linux does not support creating a single multi-arch .so file via standard tools.
# This script creates separate .so files for each architecture.

# First, check if docker is available on PATH
if ! command -v docker &> /dev/null
then
    echo "Docker is not installed. Please install Docker to continue."
    exit 1
fi

echo "Compiling x86..."
docker run --rm dockcross/linux-x86 > dockcross && chmod +x dockcross
./dockcross /bin/bash -c '$CC -shared -fPIC -m32 -o libc_wrapper_x86.so linux_libc_wrapper.c'

echo "Compiling x64..."
docker run --rm dockcross/linux-x64 > dockcross && chmod +x dockcross
./dockcross /bin/bash -c '$CC -shared -fPIC -m64 -o libc_wrapper_x86_64.so linux_libc_wrapper.c'

echo "Compiling ARM32..."
docker run --rm dockcross/linux-armv7 > dockcross && chmod +x dockcross
./dockcross /bin/bash -c '$CC -shared -fPIC -march=armv7-a -o libc_wrapper_arm32.so linux_libc_wrapper.c'

echo "Compiling ARM64..."
docker run --rm dockcross/linux-arm64 > dockcross && chmod +x dockcross
./dockcross /bin/bash -c '$CC -shared -fPIC -march=armv8-a -o libc_wrapper_arm64.so linux_libc_wrapper.c'

chmod 777 libc_wrapper_x86.so
chmod 777 libc_wrapper_x86_64.so
chmod 777 libc_wrapper_arm32.so
chmod 777 libc_wrapper_arm64.so
rm dockcross
echo "Compilation complete. Generated .so files for x86, x86_64, ARM32, and ARM64."