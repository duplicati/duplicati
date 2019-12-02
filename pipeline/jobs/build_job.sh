#!/bin/bash
SCRIPT_DIR="$( cd "$(dirname "$0")" ; pwd -P )"
. "${SCRIPT_DIR}/params.sh"

${ROOT_DIR}/pipeline/stage_build/trigger.sh \
${FORWARD_OPTS[@]} \
--sourcedir "${ROOT_DIR}" \
--targetdir "${BUILD_DIR}" | ts
