#!/bin/bash
. error_handling.sh
. ./pipeline/shared/duplicati.sh

PACKAGES="libgtk2.0-cil rsync"
docker-run --image "${MONO_IMAGE}" \
--packages "$PACKAGES" \
--command "/pipeline/stage_build/job.sh" $@
