#!/bin/bash

# This file is a workaround to install a directory recursively,
#  without hitting limitations of the SPEC file build environment

find "$1" -mindepth 1 -type d -printf '%P\0' | while IFS= read -r -d '' file; do
	install -p -d -m 755 "$2/${file}"
done

find "$1" -type f -printf '%P\0' | while IFS= read -r -d '' file; do
	install -p -D -m 644 "$1/${file}" "$2/${file}"
done
