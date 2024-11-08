#!/bin/bash
if /bin/launchctl list "com.duplicati.server.launchagent" &> /dev/null; then
    /bin/launchctl unload "/Library/LaunchAgents/com.duplicati.server.launchagent.plist"
fi

if [ -f "/Library/LaunchAgents/com.duplicati.server.launchagent.plist" ]; then
	rm "/Library/LaunchAgents/com.duplicati.server.launchagent.plist"
fi

if [ -d /usr/local/duplicati ]; then
    rm -rf /usr/local/duplicati
fi

pkgutil --forget "com.duplicati.cli"
pkgutil --forget "com.duplicati.server.daemon"


