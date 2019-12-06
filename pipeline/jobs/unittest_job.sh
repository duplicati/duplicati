#!/bin/bash
SCRIPT_DIR="$( cd "$(dirname "$0")" ; pwd -P )"
. "${SCRIPT_DIR}/_imports.sh"

${ROOT_DIR}/pipeline/stage_unittests/trigger.sh \
${FORWARD_OPTS[@]} \
--sourcedir $BUILD_DIR \
--targetdir $TEST_DIR | ts
