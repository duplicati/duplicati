#!/bin/bash
if [ -d /Applications/Duplicati.app ]; then
    rm -rf /Applications/Duplicati.app
fi

if /bin/launchctl list "com.duplicati.app.launchagent" &> /dev/null; then
    /bin/launchctl unload "/Library/LaunchAgents/com.duplicati.app.launchagent.plist"
fi

if [ -f "/Library/LaunchAgents/com.duplicati.app.launchagent.plist" ]; then
	rm "/Library/LaunchAgents/com.duplicati.app.launchagent.plist"
fi

pkgutil --forget "com.duplicati.app"
pkgutil --forget "com.duplicati.app.daemon"


