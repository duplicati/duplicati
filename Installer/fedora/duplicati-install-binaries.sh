#!/bin/bash
# This file helps mark executables as executable and symlink them to the correct location
# Lines starting with REPL: are repeated for each file in the executable list
# The values %SOURCE% and %TARGET% are replaced with the source and target (symlink) file names

# Fix permissions
REPL: chmod 755 $1/%SOURCE%

# Setup symlinks
REPL: ln -s $1/lib/%SOURCE% $2/%TARGET%
