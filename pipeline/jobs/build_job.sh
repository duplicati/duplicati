#!/bin/bash
SCRIPT_DIR="$( cd "$(dirname "$0")" ; pwd -P )"
. "${SCRIPT_DIR}/_imports.sh"

${ROOT_DIR}/pipeline/stage_build/trigger.sh \
--sourcedir "${ROOT_DIR}" \
--targetdir "${BUILD_DIR}" $@ | ts
