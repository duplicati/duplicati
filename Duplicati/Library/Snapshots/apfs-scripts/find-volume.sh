#!/bin/bash
# Find the APFS volume for a given path
# Usage: find-volume.sh <path>

PATH_TO_CHECK="$1"

if [ -z "$PATH_TO_CHECK" ]; then
    echo "Error: No path provided"
    exit 1
fi

# Get the device for the path
DEVICE=$(df -P "$PATH_TO_CHECK" | tail -1 | awk '{print $1}')

if [ -z "$DEVICE" ]; then
    echo "Error: Could not determine device for $PATH_TO_CHECK"
    exit 1
fi

# Check if it is an APFS volume
# diskutil info returns "File System Personality:   APFS" or similar
IS_APFS=$(diskutil info "$DEVICE" | grep "File System Personality" | grep "APFS")

if [ -n "$IS_APFS" ]; then
    # Return device and mount point separated by pipe
    MOUNT_POINT=$(df -P "$PATH_TO_CHECK" | tail -1 | awk '{print $6}')
    # On macOS df output might be different, let's be careful.
    # df -P output: Filesystem 512-blocks Used Available Capacity Mounted on
    # /dev/disk1s1 ... ... ... ... /
    # So $1 is device, $6 is mount point (if no spaces).
    # If mount point has spaces, it's the rest of the line.
    
    # Better way to get mount point:
    MOUNT_POINT=$(mount | grep "^$DEVICE " | sed -E 's/^.* on (.*) \(.*$/\1/')
    
    echo "$DEVICE|$MOUNT_POINT"
    exit 0
else
    echo "Error: Not an APFS volume ($DEVICE)"
    exit 1
fi
