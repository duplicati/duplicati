@echo off

REM ###############################################################################
REM How to run scripts before or after backups
REM ###############################################################################

REM Duplicati is able to run scripts before and after backups. This 
REM functionality is available in the advanced options of any backup job (UI) or
REM as option (CLI). The (advanced) options to run scripts are
REM --run-script-before = <filename>
REM --run-script-before-required = <filename>
REM --run-script-timeout = <time>
REM --run-script-after = <filename>
REM
REM --run-script-before = <filename>
REM Duplicati will run the script before the backup job and waits for its 
REM completion for 60 seconds (default timeout value). After a timeout a 
REM warning is logged and the backup is started.
REM The following exit codes are supported:
REM
REM - 0: OK, run operation
REM - 1: OK, don't run operation
REM - 2: Warning, run operation
REM - 3: Warning, don't run operation
REM - 4: Error, run operation
REM - 5: Error don't run operation
REM - other: Error don't run operation
REM
REM --run-script-before-required = <filename>
REM Duplicati will run the script before the backup job and wait for its 
REM completion for 60 seconds (default timeout value). The backup will only be
REM run if the script completes with the exit code 0. Other exit codes or a
REM timeout will cancel the backup job.
REM
REM --run-script-timeout = <time>
REM Specify a new value for the timeout. Default is 60s. Accepted values are
REM e.g. 30s, 1m15s, 1h12m03s, and so on. To turn off the timeout set the value 
REM to 0. Duplicati will then wait endlessly for the script to finish.
REM
REM --run-script-after = <filename>
REM Duplicati will run the script after the backup job and wait for its 
REM completion for 60 seconds (default timeout value). After a timeout a 
REM warning is logged.
REM The same exit codes as in --run-script-before are supported, but
REM the operation will always continue (i.e. 1 => 0, 3 => 2, 5 => 4)
REM as it has already completed so stopping it during stop is useless.



REM ###############################################################################
REM Changing options from within the script 
REM ###############################################################################

REM Within a script, all Duplicati options are exposed as environment variables
REM with the prefix "DUPLICATI__". Please notice that the dash (-) character is
REM not allowed in environment variable keys, so it is replaced with underscore
REM (_). For a list of available options, have a look at the output of
REM "duplicati.commandline.exe help".
REM
REM For instance the current value of the option --encryption-module can be 
REM accessed in the script by
REM ENCRYPTIONMODULE=%DUPLICATI__encryption_module%

REM All Duplicati options can be changed by the script by writing options to
REM stdout (with echo or similar). Anything not starting with a double dash (--)
REM will be ignored:
REM echo "Hello! -- test, this line is ignored"
REM echo --new-option=This will be a setting

REM Filters are supplied in the DUPLICATI__FILTER variable.
REM The variable contains all filters supplied with --include and --exclude,
REM combined into a single string, separated with semicolon (;).
REM Filters set with --include will be prefixed with a plus (+),
REM and filters set with --exclude will be prefixed with a minus (-).
REM
REM Example:
REM     --include=*.txt --exclude=[.*\.abc] --include=*
REM 
REM Will be encoded as:
REM     DUPLICATI__FILTER=+*.txt;-[.*\.abc];+*
REM
REM You can set the filters by writing --filter=<new filter> to stdout.
REM You may want to append to the existing filter like this:
REM     echo "--filter=+*.123;%DUPLICATI__FILTER%;-*.xyz"


REM ###############################################################################
REM Special Environment Variables
REM ###############################################################################

REM DUPLICATI__EVENTNAME
REM Eventname is BEFORE if invoked as --run-script-before, and AFTER if 
REM invoked as --run-script-after. This value cannot be changed by writing
REM it back!

REM DUPLICATI__OPERATIONNAME
REM Operation name can be any of the operations that Duplicati supports. For
REM example it can be "Backup", "Cleanup", "Restore", or "DeleteAllButN".
REM This value cannot be changed by writing it back!

REM DUPLICATI__RESULTFILE
REM If invoked as --run-script-after this will contain the name of the 
REM file where result data is placed. This value cannot be changed by 
REM writing it back!

REM DUPLICATI__REMOTEURL
REM This is the remote url for the target backend. This value can be changed by
REM echoing --remoteurl = "new value".

REM DUPLICATI__LOCALPATH
REM This is the path to the folders being backed up or restored. This variable
REM is empty operations  other than backup or restore. The local path can 
REM contain : to separate multiple folders. This value can be changed by echoing
REM --localpath = "new value".

REM DUPLICATI__PARSED_RESULT
REM This is a value indicating how well the operation was performed.
REM It can take the values: Unknown, Success, Warning, Error, Fatal.


REM ###############################################################################
REM Example script
REM ###############################################################################

REM We read a few variables first.
SET EVENTNAME=%DUPLICATI__EVENTNAME%
SET OPERATIONNAME=%DUPLICATI__OPERATIONNAME%
SET REMOTEURL=%DUPLICATI__REMOTEURL%
SET LOCALPATH=%DUPLICATI__LOCALPATH%

REM Basic setup, we use the same file for both before and after,
REM so we need to figure out which event has happened
if "%EVENTNAME%" == "BEFORE" GOTO ON_BEFORE
if "%EVENTNAME%" == "AFTER" GOTO ON_AFTER

REM This should never happen, but there may be new operations
REM in new version of Duplicati
REM We write this to stderr, and it will show up as a warning in the logfile
echo Got unknown event "%EVENTNAME%", ignoring 1>&2
GOTO end

:ON_BEFORE

REM If the operation is a backup starting, 
REM then we check if the --dblock-size option is unset
REM or 50mb, and change it to 25mb, otherwise we 
REM leave it alone

IF "%OPERATIONNAME%" == "Backup" GOTO ON_BEFORE_BACKUP
REM This will be ignored
echo Got operation "%OPERATIONNAME%", ignoring
GOTO end

:ON_BEFORE_BACKUP
REM Check if volsize is either not set, or set to 50mb
IF "%DUPLICATI__dblock_size%" == "" GOTO SET_VOLSIZE
IF "%DUPLICATI__dblock_size%" == "50mb" GOTO SET_VOLSIZE

REM We write this to stderr, and it will show up as a warning in the logfile
echo Not setting volumesize, it was already set to %DUPLICATI__dblock_size% 1>&2
GOTO end

:SET_VOLSIZE
REM Write the option to stdout to change it
echo --dblock-size=25mb
GOTO end


:ON_AFTER

IF "%OPERATIONNAME%" == "Backup" GOTO ON_AFTER_BACKUP
REM This will be ignored
echo "Got operation "%OPERATIONNAME%", ignoring	"
GOTO end

:ON_AFTER_BACKUP

REM Basic email setup		
SET EMAIL="admin@example.com"		
SET SUBJECT="Duplicati backup"

REM We use a temp file to store the email body
SET MESSAGE="%TEMP%\duplicati-mail.txt"
echo Duplicati finished a backup. > %MESSAGE%
echo This is the result : >> %MESSAGE%
echo.  >> %MESSAGE%

REM We append the results to the message
type "%DUPLICATI__RESULTFILE%" >> %MESSAGE%

REM If the log-file is enabled, we append that as well
IF EXIST "%DUPLICATI__log_file%" type "%DUPLICATI__log_file%" >> %MESSAGE%

REM If the backend-log-database file is enabled, we append that as well
IF EXIST "%DUPLICATI__backend_log_database%" type "%DUPLICATI__backend_log_database%" >> %MESSAGE%

REM Finally send the email using a fictive sendmail program
sendmail %SUBJECT% %EMAIL% < %MESSAGE%

GOTO end

:end

REM We want the exit code to always report success.
REM For scripts that can abort execution, use the option
REM --run-script-on-start-required = <filename> when running Duplicati
exit /B 0
