#!/bin/bash

# This script removes a lvm snapshot, modify as required
# 
# Input is always:
#  $1 = name of the snapshot, a random name, same as given to create-lvm-snapshot, usually something like "duplicati-0102030011234567"
#  $2 = name of the device for which the snapshot was created, same as given to create-lvm-snapshot, usually a LVM id like "vg_name/lv_name"
#  $3 = name of the temporary folder reported by create-lvm-snapshot, usually "/tmp/duplicati-0102030011234567"

NAME=$1
DEVICE=$2
TMPDIR=$3

#
# Get the reported mount point for the temp folder
#
VOLUME=`df -P "$TMPDIR" | tail -1 | awk '{ print $1}'`
if [ "$?" -ne 0 ]
then
	EXIT_CODE=$?
	echo "Error: unable to determine mount point for $TMPDIR"
	exit $EXIT_CODE
fi

#
# Get the LVMID for the temp folder
#
LVMID=`lvs "$VOLUME" --options vg_name,lv_name --noheadings | tail -1 | awk '{ print $1 "/" $2}'`
if [ "$?" -ne 0 ]
then
	EXIT_CODE=$?
	echo "Error: unable to determine LVM id for $VOLUME"
	exit $EXIT_CODE
fi

LVM_ORIGIN=`lvs "$LVMID" --noheadings --options origin | tail -1 | awk '{print $LF}'`
if [ "$?" -ne 0 ]
then
	EXIT_CODE=$?
	echo "Error: unable to determine LVM origin for $LVMID"
	exit $EXIT_CODE
fi

if [ -z "$LVM_ORIGIN" ]
then
	echo "Error: The origin of $LVMID is empty, indicating it is NOT a shapshot, aborting!"
	exit -1
fi

umount -f "$TMPDIR"
if [ "$?" -ne 0 ]
then
	echo "Error: umount -f \"$TMPDIR\" failed!"
	echo "Waiting 5 seconds before retry"

	sleep 5

	umount -f "$TMPDIR"
	EXIT_CODE=$?
	if [ "$?" -ne 0 ]
	then
			echo "Error: umount -f \"$TMPDIR\" failed twice!"
			exit $EXIT_CODE
	fi
fi

lvremove --force "$LVMID"
if [ "$?" -ne 0 ]
then
	EXIT_CODE=$?
	echo "Error: lvremove failed: lvremove --force \"$LVMID\""
	exit $EXIT_CODE
fi

rmdir "$TMPDIR"
if [ "$?" -ne 0 ]
then
	EXIT_CODE=$?
	echo "Error: rmdir \"$TMPDIR\" failed!"
	exit $EXIT_CODE
fi

exit 0