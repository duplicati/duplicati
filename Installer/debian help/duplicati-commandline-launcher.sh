#!/bin/bash
export LD_LIBRARY_PATH="/usr/lib/duplicati${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export MONO_PATH=$MONO_PATH:/usr/lib/duplicati

EXE_FILE=/usr/lib/duplicati/Duplicati.CommandLine.exe
APP_NAME=Duplicati.CommandLine

exec -a "$APP_NAME" mono "$EXE_FILE" "$@"
