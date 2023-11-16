#!/bin/bash

###############################################################################
# How to run scripts before or after backups
###############################################################################

# Duplicati is able to run scripts before and after backups. This 
# functionality is available in the advanced options of any backup job (UI) or
# as option (CLI). The (advanced) options to run scripts are
# --run-script-before = <filename>
# --run-script-before-required = <filename>
# --run-script-timeout = <time>
# --run-script-after = <filename>
#
# --run-script-before = <filename>
# Duplicati will run the script before the backup job and waits for its 
# completion for 60 seconds (default timeout value). After a timeout a 
# warning is logged and the backup is started.
# The following exit codes are supported:
#
# - 0: OK, run operation
# - 1: OK, don't run operation
# - 2: Warning, run operation
# - 3: Warning, don't run operation
# - 4: Error, run operation
# - 5: Error don't run operation
# - other: Error don't run operation
#
# --run-script-before-required = <filename>
# Duplicati will run the script before the backup job and wait for its 
# completion for 60 seconds (default timeout value). The backup will only be
# run if the script completes with the exit code 0. Other exit codes or a
# timeout will cancel the backup job.
#
# --run-script-timeout = <time>
# Specify a new value for the timeout. Default is 60s. Accepted values are
# e.g. 30s, 1m15s, 1h12m03s, and so on. To turn off the timeout set the value 
# to 0. Duplicati will then wait endlessly for the script to finish.
#
# --run-script-after = <filename>
# Duplicati will run the script after the backup job and wait for its 
# completion for 60 seconds (default timeout value). After a timeout a 
# warning is logged.
# The same exit codes as in --run-script-before are supported, but
# the operation will always continue (i.e. 1 => 0, 3 => 2, 5 => 4)
# as it has already completed so stopping it during stop is useless.


###############################################################################
# Changing options from within the script 
###############################################################################

# Within a script, all Duplicati options are exposed as environment variables
# with the prefix "DUPLICATI__". Please notice that the dash (-) character is
# not allowed in environment variable keys, so it is replaced with underscore
# (_). For a list of available options, have a look at the output of
# "duplicati.commandline.exe help".
#
# For instance the current value of the option --encryption-module can be 
# accessed in the script by
# ENCRYPTIONMODULE=$DUPLICATI__encryption_module

# All Duplicati options can be changed by the script by writing options to
# stdout (with echo or similar). Anything not starting with a double dash (--)
# will be ignored:
# echo "Hello! -- test, this line is ignored"
# echo "--new-option=\"This will be a setting\""

# Filters are supplied in the DUPLICATI__FILTER variable.
# The variable contains all filters supplied with --include and --exclude,
# combined into a single string, separated with colon (:).
# Filters set with --include will be prefixed with a plus (+),
# and filters set with --exclude will be prefixed with a minus (-).
#
# Example:
#     --include=*.txt --exclude=[.*\.abc] --include=*
# 
# Will be encoded as:
#     DUPLICATI__FILTER=+*.txt:-[.*\.abc]:+*
#
# You can set the filters by writing --filter=<new filter> to stdout.
# You may want to append to the existing filter like this:
#     echo "--filter=+*.123:%DUPLICATI__FILTER%:-*.xyz"


###############################################################################
# Special Environment Variables
###############################################################################

# DUPLICATI__EVENTNAME
# Eventname is BEFORE if invoked as --run-script-before, and AFTER if 
# invoked as --run-script-after. This value cannot be changed by writing
# it back!

# DUPLICATI__OPERATIONNAME
# Operation name can be any of the operations that Duplicati supports. For
# example it can be "Backup", "Cleanup", "Restore", or "DeleteAllButN".
# This value cannot be changed by writing it back!

# DUPLICATI__RESULTFILE
# If invoked as --run-script-after this will contain the name of the 
# file where result data is placed. This value cannot be changed by 
# writing it back!

# DUPLICATI__REMOTEURL
# This is the remote url for the target backend. This value can be changed by
# echoing --remoteurl = "new value".

# DUPLICATI__LOCALPATH
# This is the path to the folders being backed up or restored. This variable
# is empty operations  other than backup or restore. The local path can 
# contain : to separate multiple folders. This value can be changed by echoing
# --localpath = "new value".

# DUPLICATI__PARSED_RESULT
# This is a value indicating how well the operation was performed.
# It can take the values: Unknown, Success, Warning, Error, Fatal.



###############################################################################
# Example script
###############################################################################

# We read a few variables first.
EVENTNAME=$DUPLICATI__EVENTNAME
OPERATIONNAME=$DUPLICATI__OPERATIONNAME
REMOTEURL=$DUPLICATI__REMOTEURL
LOCALPATH=$DUPLICATI__LOCALPATH

# Basic setup, we use the same file for both before and after,
# so we need to figure out which event has happened
if [ "$EVENTNAME" == "BEFORE" ]
then
	# If the operation is a backup starting, 
	# then we check if the --dblock-size option is unset
	# or 50mb, and change it to 25mb, otherwise we 
	# leave it alone
	
	if [ "$OPERATIONNAME" == "Backup" ]
	then
		if [ "$DUPLICATI__dblock_size" == "" ] || ["$DUPLICATI__dblock_size" == "50mb"]
		then
			# Write the option to stdout to change it
			echo "--dblock-size=25mb"
		else
			# We write this to stderr, and it will show up as a warning in the logfile
			echo "Not setting volumesize, it was already set to $DUPLICATI__dblock_size" >&2
		fi
	else
		# This will be ignored
		echo "Got operation \"OPERATIONNAME\", ignoring"	
	fi

elif [ "$EVENTNAME" == "AFTER" ]
then

	# If this is a finished backup, we send an email
	if [ "$OPERATIONNAME" == "Backup" ]
	then

		# Basic email setup		
		EMAIL="admin@example.com"		
		SUBJECT="Duplicati backup"
		
		# We use a temp file to store the email body
		MESSAGE="/tmp/duplicati-mail.txt"
		echo "Duplicati finished a backup."> $MESSAGE
		echo "This is the result :" >> $MESSAGE
		echo "" >> $MESSAGE

		# We append the result of the operation to the email
		cat "$DUPLICATI__RESULTFILE" >> $MESSAGE

		# If the log-file is enabled, we append it
		if [ -f "$DUPLICATI__log_file" ]
		then
			echo "The log file looks like this: " >> $MESSAGE
			cat "$DUPLICATI__log_file" >> $MESSAGE
		fi
		
		# If the backend-log-database file is enabled, we append that as well
		if [ -f "$DUPLICATI__backend_log_database" ]
		then
			echo "The backend-log file looks like this: " >> $MESSAGE
			cat "$DUPLICATI__backend_log_database" >> $MESSAGE
		fi

		# Finally send the email using /bin/mail
		/bin/mail -s "$SUBJECT" "$EMAIL" < $MESSAGE
	else
		# This will be ignored
		echo "Got operation \"OPERATIONNAME\", ignoring"	
	fi
else
	# This should never happen, but there may be new operations
	# in new version of Duplicati
	# We write this to stderr, and it will show up as a warning in the logfile
	echo "Got unknown event \"$EVENTNAME\", ignoring" >&2
fi

# We want the exit code to always report success.
# For scripts that can abort execution, use the option
# --run-script-before-required = <filename> when running Duplicati
exit 0
