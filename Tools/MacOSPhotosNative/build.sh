#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="${SCRIPT_DIR}/build"
mkdir -p "${BUILD_DIR}/arm64" "${BUILD_DIR}/x86_64"

SRC="${SCRIPT_DIR}/DuplicatiPhotos.m"
HEADER="${SCRIPT_DIR}/DuplicatiPhotos.h"
OUTPUT="${SCRIPT_DIR}/libDuplicatiPhotos.dylib"

compile_arch() {
  local arch="$1"
  local destination="$2"
  echo "Building ${arch} variant..."
  xcrun -sdk macosx clang -fobjc-arc -arch "${arch}" -dynamiclib "${SRC}" -o "${destination}" \
    -framework Foundation -framework Photos
}

compile_arch arm64 "${BUILD_DIR}/arm64/libDuplicatiPhotos.dylib"
compile_arch x86_64 "${BUILD_DIR}/x86_64/libDuplicatiPhotos.dylib"

echo "Creating universal binary..."
lipo -create -output "${OUTPUT}" \
  "${BUILD_DIR}/arm64/libDuplicatiPhotos.dylib" \
  "${BUILD_DIR}/x86_64/libDuplicatiPhotos.dylib"

echo "Universal library available at ${OUTPUT}"
