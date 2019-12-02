#!/bin/bash
. error_handling.sh

PACKAGES="wget unzip rsync"
docker-run --image mono \
--packages "$PACKAGES" \
--command "/pipeline/stage_unittests/job.sh" "$@"