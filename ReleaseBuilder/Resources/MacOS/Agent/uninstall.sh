#!/bin/bash
LAUNCHAGENT_LABEL="com.duplicati.agent.launchagent"
LAUNCHAGENT_PLIST="/Library/LaunchAgents/$LAUNCHAGENT_LABEL.plist"

ANY_ULOAD_FAILURES=""

if [ -f "$LAUNCHAGENT_PLIST" ]; then

    # Loop through all logged-in users
    for USER_ID in $(ps aux | awk '/loginwindow/ && !/awk/ {print $1}' | uniq | xargs id -u); do

        # Check if the LaunchAgent is loaded for the user
        launchctl print gui/$USER_ID/$LAUNCHAGENT_LABEL > /dev/null 2>&1
        if [ $? -eq 0 ]; then
            # Unload the LaunchAgent for the user
            launchctl bootout gui/$USER_ID "$LAUNCHAGENT_PLIST"
            if [ $? -eq 0 ]; then
                echo "Successfully unloaded LaunchAgent for user with UID: $USER_ID."
            else
                echo "Failed to unload LaunchAgent for user with UID: $USER_ID."
                ANY_ULOAD_FAILURES="1"
            fi
        fi
    done

    # Legacy unload, don't care about the result
    /bin/launchctl unload "$LAUNCHAGENT_PLIST" > /dev/null 2>&1

	rm "$LAUNCHAGENT_PLIST"

    if ! [ -z "$ANY_UNLOAD_FAILURES"  ]; then
        echo "Failed to unload LaunchAgent for one or more users. A machine restart may be required to fully remove the LaunchAgent."
    fi
fi

if [ -d /usr/local/duplicati-agent ]; then
    rm -rf /usr/local/duplicati-agent
    echo "Removed /usr/local/duplicati-agent"
fi

pkgutil --forget "com.duplicati.agent"
pkgutil --forget "com.duplicati.agent.daemon"


