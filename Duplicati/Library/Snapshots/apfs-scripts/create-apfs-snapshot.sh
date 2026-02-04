#!/bin/bash

# Input:
#  $1 = snapshot name (e.g., "duplicati-abc123")
#  $2 = device/volume path (e.g., "/dev/disk1s1")
#  $3 = temporary directory prefix (e.g., "/tmp/")

NAME=$1
DEVICE=$2
TMPDIR=$3$NAME

# Get the mount point for the device
MOUNT_POINT=$(mount | grep "^$DEVICE " | sed -E 's/^.* on (.*) \(.*$/\1/')

if [ -z "$MOUNT_POINT" ]; then
    echo "Error: Could not find mount point for device $DEVICE"
    exit 1
fi

# Create APFS snapshot using tmutil
OUTPUT=$(tmutil localsnapshot "$MOUNT_POINT")
if [ "$?" -ne 0 ]; then
    echo "Error: Failed to create APFS snapshot"
    exit 1
fi

# Extract date from output
# Output format: Created local snapshot with date: 2026-02-04-134432
CREATED_DATE=$(echo "$OUTPUT" | grep -oE '[0-9]{4}-[0-9]{2}-[0-9]{2}-[0-9]{6}')

if [ -z "$CREATED_DATE" ]; then
    echo "Error: Could not parse created snapshot date from output: $OUTPUT"
    exit 1
fi

# Get the snapshot name (tmutil creates snapshots with timestamps)
# We need to find the most recent snapshot for this volume
# The format is: com.apple.TimeMachine.2026-02-04-125719.local
SNAPSHOT_FULL=$(tmutil listlocalsnapshots "$MOUNT_POINT" | tail -1)
SNAPSHOT_NAME="$SNAPSHOT_FULL"

if [ -z "$SNAPSHOT_NAME" ]; then
    echo "Error: Could not find created snapshot"
    exit 1
fi

# Verify that the found snapshot matches the created date
if [[ "$SNAPSHOT_NAME" != *"$CREATED_DATE"* ]]; then
    echo "Error: Found snapshot ($SNAPSHOT_NAME) does not match created date ($CREATED_DATE)"
    tmutil deletelocalsnapshots "$CREATED_DATE"
    exit 1
fi

# Create mount point
mkdir -p "$TMPDIR"

# Mount snapshot (requires root)
# mount_apfs [-s snapshot] <volume | device> <directory>
# The -s option takes the snapshot name, then we need the device and mount point
mount_apfs -nobrowse -s "$SNAPSHOT_NAME" -o rdonly "$DEVICE" "$TMPDIR"
if [ "$?" -ne 0 ]; then
    # Cleanup if mount fails
    if [ -n "$CREATED_DATE" ]; then
        tmutil deletelocalsnapshots "$CREATED_DATE"
    else
        # Fallback: Extract date from snapshot name
        SNAPSHOT_DATE=$(echo "$SNAPSHOT_NAME" | sed -E 's/^com\.apple\.TimeMachine\.//; s/\.local$//')
        tmutil deletelocalsnapshots "$SNAPSHOT_DATE"
    fi
    rmdir "$TMPDIR"
    echo "Error: Failed to mount APFS snapshot"
    exit 1
fi

echo "tmpdir=\"$TMPDIR\""
echo "snapshot_name=\"$SNAPSHOT_NAME\""
exit 0
