#!/bin/sh
CONF=/etc/config/qpkg.conf
QPKG_NAME=Duplicati
QPKG_ROOT=`/sbin/getcfg $QPKG_NAME Install_Path -f ${CONF}`
export QNAP_QPKG=$QPKG_NAME

DAEMON_BIN="/app/duplicati-server"
ROOTFS="$QPKG_ROOT/rootfs"
PID_FILE="$QPKG_ROOT/duplicati.pid"

# Ephemeral data: log file and temp folder. Lives inside the package
# install path and is wiped when the package is uninstalled.
DATA_DIR="$QPKG_ROOT/data"
LOG_FILE="$DATA_DIR/duplicati.log"

# Persistent data: the server datafolder (backup database, settings) and
# the database encryption key. QTS only removes the package install path
# on uninstall, so a hidden folder on the system volume survives both
# uninstalls and updates. The defVolMP lookup is the officially documented
# way to find the system volume path (handles CACHEDEV/MD0/HDA layouts);
# fall back to the volume the package itself is installed on.
SYS_VOL_PATH=$(/sbin/getcfg SHARE_DEF defVolMP -f /etc/config/def_share.info 2>/dev/null)
if [ -z "$SYS_VOL_PATH" ] || [ ! -d "$SYS_VOL_PATH" ]; then
    SYS_VOL_PATH=$(dirname "$(dirname "$QPKG_ROOT")")
fi
PERSIST_DIR="$SYS_VOL_PATH/.qpkg_data/$QPKG_NAME"

APACHE_CONF_DIR="/etc/config/apache/extra"
APACHE_CONF_FILE="$APACHE_CONF_DIR/duplicati.conf"
HTTP_SYS_PROXY_CONF="/etc/apache-sys-proxy.conf"
HTTPS_SYS_PROXY_CONF="/etc/apache-sys-proxy-ssl.conf"
HTTP_PROXY_BIN="/usr/local/apache/bin/apache_proxy"
HTTPS_PROXY_BIN="/usr/local/apache/bin/apache_proxys"

reload_apache() {
    if [ -x "$HTTP_PROXY_BIN" ]; then
        "$HTTP_PROXY_BIN" -k graceful -f "$HTTP_SYS_PROXY_CONF" 2>/dev/null || killall -HUP apache_proxy 2>/dev/null || true
    fi

    if [ -x "$HTTPS_PROXY_BIN" ]; then
        "$HTTPS_PROXY_BIN" -k graceful -f "$HTTPS_SYS_PROXY_CONF" 2>/dev/null || killall -HUP apache_proxys 2>/dev/null || true
    fi
}

setup_apache() {
    # Register the proxy route with the QTS system proxy config; requests must
    # pass the QTS session auth, and then get a pre-auth token injected,
    # skipping the Duplicati login
    mkdir -p "$APACHE_CONF_DIR"

    # Define location block for port 8080 system proxy.
    # The X-Forwarded-Prefix header tells the server the public path so it
    # can rewrite the SPA base href and asset links accordingly.
    # Note: neither the Location nor the ProxyPass target may end with a
    # slash, otherwise the backend receives double-slash paths
    cat <<EOF >"$APACHE_CONF_FILE"
<Location "/apps/duplicati">
    ProxyPass http://127.0.0.1:8200 upgrade=websocket
    ProxyPassReverse http://127.0.0.1:8200
    RequestHeader set X-Forwarded-Prefix "/apps/duplicati"
    RequestHeader set Authorization "PreAuth $PREAUTH_TOKEN"
</Location>
EOF

    # The config contains the pre-auth token, restrict read access
    chmod 600 "$APACHE_CONF_FILE" 2>/dev/null || true

    # Include in system proxy config if missing
    if [ -f "$HTTP_SYS_PROXY_CONF" ] && ! grep -q "duplicati.conf" "$HTTP_SYS_PROXY_CONF" 2>/dev/null; then
        echo "Include $APACHE_CONF_FILE" >>"$HTTP_SYS_PROXY_CONF"
    fi

    if [ -f "$HTTPS_SYS_PROXY_CONF" ] && ! grep -q "duplicati.conf" "$HTTPS_SYS_PROXY_CONF" 2>/dev/null; then
        echo "Include $APACHE_CONF_FILE" >>"$HTTPS_SYS_PROXY_CONF"
    fi

    reload_apache
}

teardown_apache() {
    # Unregister the proxy route so requests fail cleanly when app is stopped
    rm -f "$APACHE_CONF_FILE"
    if [ -f "$HTTP_SYS_PROXY_CONF" ]; then
        sed -i '/duplicati.conf/d' "$HTTP_SYS_PROXY_CONF" 2>/dev/null || true
    fi
    if [ -f "$HTTPS_SYS_PROXY_CONF" ]; then
        sed -i '/duplicati.conf/d' "$HTTPS_SYS_PROXY_CONF" 2>/dev/null || true
    fi
    reload_apache
}

generate_random_password() {
    if command -v openssl >/dev/null 2>&1; then
        openssl rand -base64 32 | tr '+/' '-_' | tr -d '='
    else
        head -c 32 /dev/urandom | base64 | tr '+/' '-_' | tr -d '='
    fi
}

is_mounted() {
    grep -qs " $1 " /proc/mounts
}

setup_mounts() {
    mkdir -p "$ROOTFS/proc" "$ROOTFS/dev" "$ROOTFS/sys" "$ROOTFS/share" "$ROOTFS/data" "$ROOTFS/local"

    # Mount host paths into the rootfs so Duplicati can see NAS storage.
    # /data is the persistent server datafolder (survives uninstall),
    # /local is the ephemeral package data folder used for temp files
    is_mounted "$ROOTFS/proc"  || mount -o bind /proc "$ROOTFS/proc"
    is_mounted "$ROOTFS/dev"   || mount -o bind /dev "$ROOTFS/dev"
    is_mounted "$ROOTFS/sys"   || mount -o bind /sys "$ROOTFS/sys"
    is_mounted "$ROOTFS/share" || mount -o bind /share "$ROOTFS/share"
    is_mounted "$ROOTFS/data"  || mount -o bind "$PERSIST_DIR" "$ROOTFS/data"
    is_mounted "$ROOTFS/local" || mount -o bind "$DATA_DIR" "$ROOTFS/local"

    # Provide working DNS resolution inside the chroot
    cp -L /etc/resolv.conf "$ROOTFS/etc/resolv.conf"
}

teardown_mounts() {
    for m in local data share sys dev proc; do
        if is_mounted "$ROOTFS/$m"; then
            umount "$ROOTFS/$m"
        fi
    done
}

start_daemon() {
    if [ ! -x "$ROOTFS$DAEMON_BIN" ]; then
        echo "$QPKG_NAME: binary not found: $ROOTFS$DAEMON_BIN" >&2
        exit 1
    fi

    if [ -f "$PID_FILE" ]; then
        PID=$(cat "$PID_FILE" 2>/dev/null)
        if [ -n "$PID" ] && kill -0 "$PID" 2>/dev/null; then
            echo "$QPKG_NAME is already running."
            exit 0
        fi
        rm -f "$PID_FILE"
    fi

    mkdir -p "$DATA_DIR" "$PERSIST_DIR"
    # Duplicati refuses to start if the data folder is group/other accessible;
    # QTS creates it with permissive defaults, so restrict it
    chmod 700 "$DATA_DIR" "$PERSIST_DIR"

    # The temp folder must exist before start (--tempdir); it intentionally
    # stays in the package data directory that is wiped on uninstall
    mkdir -p "$DATA_DIR/tmp"

    # The bundled Debian rootfs provides a recent glibc (QTS ships a very old
    # one), so the server runs in a chroot with host paths bound into it
    setup_mounts

    # Generate a fresh pre-auth token for each start. It is only written
    # into the rendered Apache config and passed to the server, never
    # persisted, and lets QTS-authenticated proxy requests skip the
    # Duplicati login
    PREAUTH_TOKEN=$(generate_random_password)

    # Generate a random web UI password on each start. Access is gated by
    # the QTS session and the pre-auth token, so the Duplicati password is
    # never user-facing; setting one (again) on every start just ensures
    # the web UI treats auth as configured and never prompts for a
    # password. Strictly speaking only the first start needs it, but
    # repeating it is harmless and keeps the value unguessable.
    WEBSERVICE_PASSWORD=$(generate_random_password)

    # Register Apache proxy route before starting service
    setup_apache

    # Generate an encryption key for the settings database, if missing.
    # The key is stored with the persistent data so a re-install can
    # still decrypt the settings database
    if [ ! -f "$PERSIST_DIR/db_enc_key" ]; then
        generate_random_password >"$PERSIST_DIR/db_enc_key"
        chmod 600 "$PERSIST_DIR/db_enc_key" 2>/dev/null || true
    fi

    DATABASE_PASSWORD=$(cat "$PERSIST_DIR/db_enc_key")

    echo "Starting $QPKG_NAME ..."
    # Secrets are passed via environment so they are not visible
    # in the process command line (/proc/*/cmdline).
    # HOME points to /share (bound into the rootfs) so the web UI shows
    # the NAS shares as the "Home" folder; the actual config lives in
    # /data (the persistent datafolder mount) via XDG_CONFIG_HOME and
    # --server-datafolder.
    HOME=/share \
        XDG_CONFIG_HOME=/data \
        SETTINGS_ENCRYPTION_KEY="$DATABASE_PASSWORD" \
        DUPLICATI__WEBSERVICE_PRE_AUTH_TOKENS="$PREAUTH_TOKEN" \
        DUPLICATI__WEBSERVICE_PASSWORD="$WEBSERVICE_PASSWORD" \
        DUPLICATI_ENABLE_IFRAME_HOSTING="true" \
        QNAP_AUTH_ENABLED="1" \
        chroot "$ROOTFS" "$DAEMON_BIN" \
        --server-datafolder=/data \
        --webservice-interface=loopback \
        --webservice-port=8200 \
        --webservice-allowed-hostnames="*" \
        --tempdir=/local/tmp \
        >>"$LOG_FILE" 2>&1 &

    echo $! >"$PID_FILE"
}

stop_daemon() {
    # Unregister Apache proxy route so requests fail cleanly when app is stopped
    teardown_apache

    if [ ! -f "$PID_FILE" ]; then
        # Still clean up mounts if the server died without removing them
        teardown_mounts
        return 0
    fi

    PID=$(cat "$PID_FILE" 2>/dev/null)
    if [ -n "$PID" ] && kill -0 "$PID" 2>/dev/null; then
        echo "Stopping $QPKG_NAME (pid=$PID) ..."
        kill "$PID" 2>/dev/null || true
        # Give it a few seconds to exit gracefully
        for i in 1 2 3 4 5 6 7 8 9 10; do
            if ! kill -0 "$PID" 2>/dev/null; then
                break
            fi
            sleep 2
        done
        if kill -0 "$PID" 2>/dev/null; then
            echo "$QPKG_NAME: process did not exit, sending SIGKILL" >&2
            kill -9 "$PID" 2>/dev/null || true
        fi
    fi

    rm -f "$PID_FILE"

    # Unmount the host paths bound into the rootfs
    teardown_mounts
}

case "$1" in
  start)
    ENABLED=$(/sbin/getcfg $QPKG_NAME Enable -u -d FALSE -f $CONF)
    if [ "$ENABLED" != "TRUE" ]; then
        echo "$QPKG_NAME is disabled."
        exit 1
    fi
    start_daemon
    ;;

  stop)
    stop_daemon
    ;;

  restart)
    $0 stop
    $0 start
    ;;

  remove)
    ;;

  *)
    echo "Usage: $0 {start|stop|restart|remove}"
    exit 1
esac

exit 0
