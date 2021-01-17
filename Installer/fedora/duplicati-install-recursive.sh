#!/bin/bash

# This file is a workaround to install a directory recursively,
#  without hitting limitations of the SPEC file build environment

OLDIFS=$IFS
IFS=$'\n'

for file in $(find "$1" -type d -printf '%P\n'); do
	install -p -d -m 755 "$2/${file}"
done

for file in $(find "$1" -type f -printf '%P\n'); do
	install -p -D -m 644 "$1/${file}" "$2/${file}"
done

IFS=$OLDIFS
