#!/bin/bash
set -e

# Extract the command to run
CMD="$@"

if [ -z "$CMD" ]; then
    echo "ERROR: No command specified to run."
    exit 1
fi

# Create user/group and drop privileges if UID/GID are provided
if [[ -n "$UID" && -n "$GID" ]]; then
    # Create group if it doesn't already exist
    if ! getent group "$GID" >/dev/null; then
        groupadd -g "$GID" duplicati
    fi

    # Create user if it doesn't already exist
    if ! id -u "$UID" >/dev/null 2>&1; then
        useradd -u "$UID" -g "$GID" -m -s /bin/bash duplicati
    fi

    # Set ownership on required paths
    chown -R "$UID:$GID" /opt/duplicati /data || true

    exec su duplicati -c "$(printf "%q " "$@")"
else
    exec "$@"
fi