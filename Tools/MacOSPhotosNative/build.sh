#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="${SCRIPT_DIR}/build"
mkdir -p "${BUILD_DIR}/arm64" "${BUILD_DIR}/x86_64"

SRC="${SCRIPT_DIR}/DuplicatiPhotos.m"
HEADER="${SCRIPT_DIR}/DuplicatiPhotos.h"
OUTPUT="${SCRIPT_DIR}/libDuplicatiPhotos.dylib"

# Oldest macOS the library may load on. Without this, clang uses the SDK
# version, so building on a newer SDK silently raises the minimum OS.
export MACOSX_DEPLOYMENT_TARGET="${MACOSX_DEPLOYMENT_TARGET:-26.0}"

compile_arch() {
  local arch="$1"
  local destination="$2"
  echo "Building ${arch} variant (minimum macOS ${MACOSX_DEPLOYMENT_TARGET})..."
  xcrun -sdk macosx clang -fobjc-arc -arch "${arch}" -mmacosx-version-min="${MACOSX_DEPLOYMENT_TARGET}" \
    -dynamiclib -install_name "@rpath/libDuplicatiPhotos.dylib" "${SRC}" -o "${destination}" \
    -framework Foundation -framework Photos
}

compile_arch arm64 "${BUILD_DIR}/arm64/libDuplicatiPhotos.dylib"
compile_arch x86_64 "${BUILD_DIR}/x86_64/libDuplicatiPhotos.dylib"

echo "Creating universal binary..."
lipo -create -output "${OUTPUT}" \
  "${BUILD_DIR}/arm64/libDuplicatiPhotos.dylib" \
  "${BUILD_DIR}/x86_64/libDuplicatiPhotos.dylib"

echo "Universal library available at ${OUTPUT}"
