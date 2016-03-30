#!/bin/bash
INSTALLDIR=/usr/lib/duplicati
export LD_LIBRARY_PATH="${INSTALLDIR}${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export MONO_PATH=$MONO_PATH:${INSTALLDIR}

EXE_FILE=${INSTALLDIR}/Duplicati.Server.exe
APP_NAME=DuplicatiServer

exec -a "$APP_NAME" mono "$EXE_FILE" "$@"
