#! /bin/bash

set -o noglob

#
# Files and folders to backup
#
# NOTE:
#  - Space in filenames is not supported, replace with wildcards as required
#  - Wilcards match the whole part as a string not filename only
#
SOURCE="/etc /root /home --exclude=*.bak --exclude=*~ --exclude=*.DS_Store --exclude=*Thumbs.db --exclude=/etc/duplicati/duplicati.sqlite*"

#
# Paths to executables used in this script
#
MONO=/usr/bin/mono
DUPLICATI=/usr/lib/duplicati/Duplicati.CommandLine.exe

#
# URL to the backup storage
#
STORAGE=ssh://yourhost.yourdomain:/home/duplicati

#
# Username for accessing the backup storage
#
AUTH_USERNAME=youraccount

#
# SSH keyfile for accessing storage
#
KEYFILE=/etc/duplicati/id_rsa

#
# Fingerprint for server verification to prevent man-in-the-middle attack
#
SSH_FINGERPRINT="ssh-rsa 2048 00:00:00:00:00:00:00:00:00:00:00:00:00:00:00:00"

#
# Path to the local database
#
DB_PATH=/etc/duplicati/duplicati.sqlite

#
# Password to the duplicati database
# The export is needed as the password is passed in environment variables and not in command line
#
export PASSPHRASE='passphrase'

#
# Block size for backup files.
#
DB_BLOCK_SIZE=100MB

#
# How long to keep old versions of the files
#
KEEP_TIME=1M

#
# Throttling of backup. Set to 0 for no throttling
#
BACKUP_THROTTLE=500MB

#
# Settings for backup verification download sycle
#
SAMPLE_COUNT=1
TEST_THROTTLE=500MB

#
# Add any additional options as needed. These options are used for all commands
#
OPTIONS="--dbpath=$DB_PATH \"--ssh-fingerprint=$SSH_FINGERPRINT\" --ssh-keyfile=$KEYFILE --auth-username=$AUTH_USERNAME --dblock-size=$DB_BLOCK_SIZE --keep-time=$KEEP_TIME --encryption-module=aes --auto-cleanup=true --disable-module='console-password-input,check-mono-ssl' --thread-priority=low"

#
# Parse command line and execute backup
#

if [ $# -lt 1 ]
then
  echo "Usage: `basename $0` {arg}"
  exit 1
fi

case $1 in
	backup)
		eval $MONO $DUPLICATI backup $STORAGE $SOURCE $OPTIONS --throttle-download=$BACKUP_THROTTLE --throttle-upload=$BACKUP_THROTTLE --no-backend-verification
		;;

	compact)
		eval $MONO $DUPLICATI compact $STORAGE $OPTIONS --auto-update=true
		;;

	find)
		eval $MONO $DUPLICATI find $STORAGE ${*:2} $OPTIONS --all-versions=true
		;;

	repair)
		eval $MONO $DUPLICATI repair $STORAGE $OPTIONS
		;;

	restore)
		eval $MONO $DUPLICATI restore $STORAGE ${*:2} $OPTIONS --restore-permissions=true
		;;

	test)
		eval $MONO $DUPLICATI test $STORAGE $SAMPLE_COUNT $OPTIONS --throttle-download=$TEST_THROTTLE --throttle-upload=$TEST_THROTTLE
		;;

	*)
		echo "Unknown command '$1'"
		exit 1;
esac
