#!/bin/bash
. "$( cd "$(dirname "$0")" ; pwd -P )/../docker-run/error_handling.sh"

export DUPLICATI_ROOT="/application/"
declare -a FORWARD_OPTS

export UPDATE_SOURCE="${DUPLICATI_ROOT}/Updates/build/${BUILDTAG}_source"
export UPDATE_TARGET="${DUPLICATI_ROOT}/Updates/build/${BUILDTAG}_target"

