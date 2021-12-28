#!/bin/bash
. error_handling.sh
. ./pipeline/shared/duplicati.sh

PACKAGES="wget unzip rsync"
docker-run --image "${MONO_IMAGE}" \
--packages "$PACKAGES" \
--command "/pipeline/stage_unittests/job.sh" $@
