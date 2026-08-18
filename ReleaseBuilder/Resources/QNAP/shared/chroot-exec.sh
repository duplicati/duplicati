#!/bin/sh
# Runs a Duplicati command-line binary inside the package chroot.
#
# QTS ships a glibc that is too old for the .NET binaries, so the package
# bundles a minimal Debian rootfs that the server runs in (see duplicati.sh).
# This script sets up the same chroot environment for one-shot command-line
# use and executes the given binary inside it. The helpers in bin/ are thin
# wrappers that call this script with their binary name.
#
# Usage: chroot-exec.sh <binary> [args...]

CONF=/etc/config/qpkg.conf
QPKG_NAME=Duplicati
QPKG_ROOT=`/sbin/getcfg $QPKG_NAME Install_Path -f ${CONF}`
export QNAP_QPKG=$QPKG_NAME

ROOTFS="$QPKG_ROOT/rootfs"

# Ephemeral package data folder, same as in duplicati.sh
DATA_DIR="$QPKG_ROOT/data"

# Persistent server datafolder on the system volume, same as in duplicati.sh
SYS_VOL_PATH=$(/sbin/getcfg SHARE_DEF defVolMP -f /etc/config/def_share.info 2>/dev/null)
if [ -z "$SYS_VOL_PATH" ] || [ ! -d "$SYS_VOL_PATH" ]; then
    SYS_VOL_PATH=$(dirname "$(dirname "$QPKG_ROOT")")
fi
PERSIST_DIR="$SYS_VOL_PATH/.qpkg_data/$QPKG_NAME"

if [ "$(id -u)" != "0" ]; then
    echo "$QPKG_NAME: must run as root (admin) to enter the chroot" >&2
    exit 1
fi

BIN="$1"
if [ -z "$BIN" ]; then
    echo "Usage: $0 <binary> [args...]" >&2
    exit 1
fi
shift

if [ ! -x "$ROOTFS/app/$BIN" ]; then
    echo "$QPKG_NAME: binary not found: $ROOTFS/app/$BIN" >&2
    exit 1
fi

is_mounted() {
    grep -qs " $1 " /proc/mounts
}

# Bind the host paths the tools need into the rootfs. Only missing mounts
# are created, and only those are removed again on exit, so the mounts of
# a running server are left untouched.
MOUNTED_BY_US=""
setup_mounts() {
    mkdir -p "$ROOTFS/proc" "$ROOTFS/dev" "$ROOTFS/sys" "$ROOTFS/share" "$ROOTFS/data" "$ROOTFS/local"
    mkdir -p "$DATA_DIR" "$PERSIST_DIR"

    for entry in "proc:/proc" "dev:/dev" "sys:/sys" "share:/share" "data:$PERSIST_DIR" "local:$DATA_DIR"; do
        name=${entry%%:*}
        src=${entry#*:}
        if ! is_mounted "$ROOTFS/$name"; then
            if mount -o bind "$src" "$ROOTFS/$name"; then
                MOUNTED_BY_US="$name $MOUNTED_BY_US"
            fi
        fi
    done

    # Provide working DNS resolution inside the chroot
    cp -L /etc/resolv.conf "$ROOTFS/etc/resolv.conf"
}

teardown_mounts() {
    for name in $MOUNTED_BY_US; do
        if is_mounted "$ROOTFS/$name"; then
            umount "$ROOTFS/$name" 2>/dev/null || true
        fi
    done
    MOUNTED_BY_US=""
}

setup_mounts
trap teardown_mounts EXIT

# The chroot command resets the working directory to /, so the desired
# directory is applied inside the chroot instead. Paths under /share are
# bound in and map 1:1; anything else runs from /share (the NAS storage).
case "$PWD" in
    /share|/share/*) INNER_CWD="$PWD" ;;
    *) INNER_CWD=/share ;;
esac

# Temp files go to the ephemeral package data folder (matching the
# server's --tempdir=/local/tmp), not the small QTS system tmpfs
mkdir -p "$DATA_DIR/tmp"

# Pass the settings database key so tools that read the server database
# can decrypt it; the key only exists after the first server start
if [ -f "$PERSIST_DIR/db_enc_key" ]; then
    SETTINGS_ENCRYPTION_KEY=$(cat "$PERSIST_DIR/db_enc_key")
    export SETTINGS_ENCRYPTION_KEY
fi

# HOME points at the NAS shares (bound into the rootfs) so the tools see
# the same file layout as the server; XDG_CONFIG_HOME keeps the configs
# in the persistent datafolder. The rootfs shell applies the working
# directory, falling back to the chroot root if it is not reachable.
HOME=/share \
    XDG_CONFIG_HOME=/data \
    TMPDIR=/local/tmp \
    chroot "$ROOTFS" /bin/sh -c 'cd "$1" 2>/dev/null || cd /; shift; exec "$@"' sh "$INNER_CWD" "/app/$BIN" "$@"
RC=$?

trap - EXIT
teardown_mounts
exit $RC
