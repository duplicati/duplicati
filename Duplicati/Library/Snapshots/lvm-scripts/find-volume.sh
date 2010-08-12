#!/bin/bash

# This script returns the device on which the volume is mounted
# 
# Input is always:
#  $1 = name of the folder to locate the LVM device for
#
# The script MUST output a line with device="<path>", where path is the lvm id.
# The script MUST output a line with mountpoint="<path>", where path is the device root.
# This ensures that any tools invoked can write info to the console,
#  and this will not interfere with the program functions.


#
# Rename the input
#
NAME=$1

#
# Get the reported mount point for the current folder
#
VOLUME=`df -P "$NAME" | tail -1 | awk '{ print $1}'`
if [ "$?" -ne 0 ]
then
	EXIT_CODE=$?
	echo "Error: unable to determine device for $NAME"
	exit $EXIT_CODE
fi

MOUNTPOINT=`df -P "$NAME" | tail -1 | awk '{ print $NF}'`
if [ "$?" -ne 0 ]
then
	EXIT_CODE=$?
	echo "Error: unable to determine mount point for $NAME"
	exit $EXIT_CODE
fi

#
# Get the LVM path for the mapped volume
#
LVMID=`lvs "$VOLUME" --options vg_name,lv_name --noheadings | tail -1 | awk '{ print $1 "/" $2}'`
if [ "$?" -ne 0 ]
then
	EXIT_CODE=$?
	echo "Error: Unable to determine volume group (VG) for mapped volume $VOLUME"
	exit $EXIT_CODE
fi

echo "mountpoint=\"$MOUNTPOINT\""
echo "device=\"$LVMID\""

exit 0