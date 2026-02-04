#!/bin/bash

# Input:
#  $1 = snapshot name (not used with tmutil, but kept for compatibility)
#  $2 = device
#  $3 = mount point

NAME=$1
DEVICE=$2
MOUNTPOINT=$3

# Unmount
umount "$MOUNTPOINT"
if [ "$?" -ne 0 ]; then
    # Try force unmount
    umount -f "$MOUNTPOINT"
fi

# Remove mount point
rmdir "$MOUNTPOINT"

# Delete all local snapshots for the device (tmutil doesn't support deleting specific snapshots easily)
# Get the mount point for the device
MOUNT_POINT=$(mount | grep "^$DEVICE " | sed -E 's/^.* on (.*) \(.*$/\1/')

if [ -n "$MOUNT_POINT" ]; then
    # Check if NAME looks like an APFS snapshot (com.apple.TimeMachine...)
    if [[ "$NAME" == com.apple.TimeMachine.* ]]; then
        # Extract date and delete specific snapshot
        SNAPSHOT_DATE=$(echo "$NAME" | sed -E 's/^com\.apple\.TimeMachine\.//; s/\.local$//')
        tmutil deletelocalsnapshots "$SNAPSHOT_DATE"
    else
        # Fallback: Delete all local snapshots for this volume
        tmutil deletelocalsnapshots "$MOUNT_POINT"
    fi
fi

exit 0
