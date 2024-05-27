#!/bin/bash
# This file helps mark executables as executable and symlink them to the correct location
# Lines starting with REPL: are repeated for each file in the executable list
# Lines starting with SYML: are repeated for each file in the executable binaries list
# The values %SOURCE% and %TARGET% are replaced with the source and target (symlink) file names

BUILDROOT=$1
EXEC_PREFIX=$2
NAMER=$3

# Fix permissions
REPL: chmod 755 ${BUILDROOT}${EXEC_PREFIX}/lib/${NAMER}/%SOURCE%

# Setup symlinks
SYML: ln -s ../lib/${NAMER}/%SOURCE% ${BUILDROOT}${EXEC_PREFIX}/bin/%TARGET%
