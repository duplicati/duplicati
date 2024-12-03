#!/bin/bash
if /bin/launchctl list "com.duplicati.agent.launchagent" &> /dev/null; then
    /bin/launchctl unload "/Library/LaunchAgents/com.duplicati.agent.launchagent.plist"
fi

if [ -f "/Library/LaunchAgents/com.duplicati.agent.launchagent.plist" ]; then
	rm "/Library/LaunchAgents/com.duplicati.agent.launchagent.plist"
fi

if [ -d /usr/local/duplicati-agent ]; then
    rm -rf /usr/local/duplicati-agent
fi

pkgutil --forget "com.duplicati.agent"
pkgutil --forget "com.duplicati.agent.daemon"


