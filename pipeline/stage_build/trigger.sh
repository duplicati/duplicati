#!/bin/bash
. error_handling.sh

PACKAGES="libgtk2.0-cil rsync"
docker-run --image mono \
--packages "$PACKAGES" \
--command "/pipeline/stage_build/job.sh" $@