#!/bin/bash
# -fobjc-arc: enables ARC
# -fmodules: enables modules so you can import with `@import AppKit;`
# -mmacosx-version-min=10.6: support older OS X versions, this might increase the binary size

if [ ! -d "bin" ]; then mkdir bin; fi

clang run-with-mono.m duplicati.m -fobjc-arc -fmodules -mmacosx-version-min=11.0 -o bin/duplicati
clang run-with-mono.m duplicati-cli.m -fobjc-arc -fmodules -mmacosx-version-min=11.0 -o bin/duplicati-cli
clang run-with-mono.m duplicati-server.m -fobjc-arc -fmodules -mmacosx-version-min=11.0 -o bin/duplicati-server