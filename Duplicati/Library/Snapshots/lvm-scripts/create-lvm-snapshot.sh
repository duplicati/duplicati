#!/bin/bash

# This script creates a lvm snapshot, modify as required
# 
# Input is always:
#  $1 = name of the snapshot, a random name with no spaces or non-printable chars, usually something like "duplicati-0102030011234567"
#  $2 = name of the device to create the snapshot for, this is the output of find-volume.sh, and usually a LVM id like "vg_name/lv_name"
#  $3 = the full path to the temporary folder used by the application, suffixed with a /, eg "/tmp/"
#
# The script MUST output a line with tmpdir="<dir>", where dir is the temporary mounted directory
# This ensures that the script can override the settings to better fit the distro, 
#  and disregard the naming conventions used normally. It also helps if the tools invoked write info
#  to the console that could be misinterpreted as the output path.

# Rename arguments to better symbols
NAME=$1
DEVICE=$2
TMPDIR=$3$NAME

# A snapshot requires that the volume has spare blocks, where changes are written.
# If the snapshot runs out of free space, it will be dropped, resulting in a partial
#  backup. The required size depends on how much data the system writes to disk while
#  the snapshot is active.
# Adjust these numbers to your system needs.
MIN_FREE_BLOCKS=20
MAX_FREE_BLOCKS=1000

#
# Get the number of free blocks on the volume
#
FREEBLOCKS=`lvs "$DEVICE" --noheadings --options vg_free_count | tail -1 | awk '{printf $NF}'`
if [ "$?" -ne 0 ]
then
	EXIT_CODE=$?
	echo "Error: lvs failed to find free space: lvs \"$DEVICE\" --noheadings --options vg_free_count"
	exit $EXIT_CODE
fi

if [ -z "$FREEBLOCKS" ]
then
        echo "Error: Unable to read free space for snapshot device \"$DEVICE\""
        exit -1
fi

#
# If there is too little space the snapshot may be dropped, 
#  resulting in an incomplete backup.
# Some users may want to change this number
#
if [ "$FREEBLOCKS" -lt "$MIN_FREE_BLOCKS" ]
then
	echo "Error: Not enough free space for snapshot device \"$DEVICE\""
	exit -1
fi

#
# If there is too much space, just take some, 
#  some users may want to change this number
#
if [ "$FREEBLOCKS" -gt "$MAX_FREE_BLOCKS" ]
then
	FREEBLOCKS=$MAX_FREE_BLOCKS
fi

LV_GROUP=`lvs "$DEVICE" --noheadings --options vg_name | tail -1 | awk '{printf $NF}'`
if [ "$?" -ne 0 ]
then
	EXIT_CODE=$?
	echo "Error: lvs failed to read VG: lvs \"$DEVICE\" --noheadings --options vg_name"
	exit $EXIT_CODE
fi


#
# Create the logical volume snapsnot
#
lvcreate --extents $FREEBLOCKS --snapshot --name "$NAME" "$DEVICE"
if [ "$?" -ne 0 ]
then
	EXIT_CODE=$?
	echo "Error: lvcreate failed: lvcreate --extents $FREEBLOCKS --snapshot --name \"$NAME\" \"$DEVICE\""
	exit $EXIT_CODE
fi

#
# Get the path to the volume snapshot
# The cases are here to support various versions of lvdisplay
#
LV_SNAPSHOT=`lvdisplay "$LV_GROUP/$NAME" | grep "LV Path" | awk '{ print $NF}'`
if [ "$?" -ne 0 ] || [ -z "$LV_SNAPSHOT" ] || [ ! -e "$LV_SNAPSHOT" ]
then
	LV_SNAPSHOT=`lvdisplay "$LV_GROUP/$NAME" | grep "LV Name" | awk '{ print $NF}'`
fi

if [ "$?" -ne 0 ] || [ -z "$LV_SNAPSHOT" ] || [ ! -e "$LV_SNAPSHOT" ]
then
	EXIT_CODE=$?
	echo "Error: Unable to determine LV path for volume $LV_GROUP/$NAME"
	
	#We have created the volume, so remove it before exit
	lvremove --force "$LV_GROUP/$NAME"
	exit $EXIT_CODE
fi

#
# Create a mount point
#
mkdir "$TMPDIR"
if [ "$?" -ne 0 ]
then
	EXIT_CODE=$?
	echo "Error: mkdir \"$TMPDIR\" failed!"
	
	#We have created the volume, so remove it before exit
	lvremove --force "$LV_GROUP/$NAME"
	exit $EXIT_CODE
fi

#
# Find filesystem used on $DEVICE.
# XFS filesystems need to be mounted with option -o nouuid.
# Other filesystems do not support that option.
#

FILESYSTEM=`df -PT /dev/"$DEVICE" | tail -1 | awk '{print $2}'`
if [ "$FILESYSTEM" == "xfs" ]; then
    MOUNT_OPTIONS="ro,nouuid"
else
    MOUNT_OPTIONS="ro"
fi

#
# Mount the snapshot on the mount point
#
mount -o "$MOUNT_OPTIONS" "$LV_SNAPSHOT" "$TMPDIR"
if [ "$?" -ne 0 ]
then
	EXIT_CODE=$?
	echo "Error: mount -o \"$MOUNTOPTIONS\" \"$LV_SNAPSHOT\" \"$TMPDIR\" failed!"

	#We have created the volume, so remove it before exit
	lvremove --force "$LV_GROUP/$NAME"
	exit $EXIT_CODE
fi

#
# Report back to the caller what the name of the snapshot volume is
#
echo "tmpdir=\"$TMPDIR\""
exit 0
