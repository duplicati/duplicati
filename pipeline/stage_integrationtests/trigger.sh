#!/bin/bash
. error_handling.sh

PACKAGES="python3-pip rsync mono-complete"
docker-run --image selenium/standalone-firefox \
--packages "$PACKAGES" \
--asroot \
--sharedmem \
--command "sudo -u seluser /pipeline/stage_integrationtests/job.sh" $@
