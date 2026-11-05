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

# Reports whether the mount at the given path, or any mount below it, uses
# shared propagation (a "shared:N" tag in /proc/self/mountinfo). Unmounting
# nested mounts of a shared tree would propagate to the host volumes, so
# such trees are never bound or unmounted recursively (see duplicati.sh)
has_shared_mounts() {
    while read -r _id _parent _dev _root mnt _opts rest; do
        case "$mnt" in
            "$1"|"$1"/*) ;;
            *) continue ;;
        esac
        case "$rest" in
            "- "*) tags="" ;;
            *) tags="${rest%% - *}" ;;
        esac
        case " $tags " in
            *" shared:"*) return 0 ;;
        esac
    done </proc/self/mountinfo
    return 1
}

# Unmount a mount point and every mount nested below it, deepest first.
# BusyBox umount has no recursive flag, so the nested mounts are read
# from /proc/mounts (a reverse sort puts children before their parents)
umount_recursive() {
    if has_shared_mounts "$1"; then
        echo "$QPKG_NAME: $1 uses shared mount propagation, leaving nested mounts alone" >&2
    else
        while read -r _dev mnt _rest; do
            case "$mnt" in
                "$1"/*) echo "$mnt" ;;
            esac
        done </proc/mounts | sort -r | while read -r mnt; do
            umount "$mnt" 2>/dev/null || true
        done
    fi

    if is_mounted "$1"; then
        umount "$1" 2>/dev/null || true
    fi
}

# Bind /share recursively so the volumes mounted beneath it are visible
# too, unless the host uses shared propagation (see duplicati.sh)
bind_share() {
    if has_shared_mounts /share; then
        echo "$QPKG_NAME: /share uses shared mount propagation, nested volumes will not be visible" >&2
        mount -o bind /share "$1"
        return
    fi
    mount -o rbind /share "$1"
}

# Bind the host paths the tools need into the rootfs. Only missing mounts
# are created, and only those are removed again on exit, so the mounts of
# a running server are left untouched. /share goes first so the recursive
# bind does not pick up the other rootfs mounts, which live under /share
MOUNTED_BY_US=""
setup_mounts() {
    mkdir -p "$ROOTFS/proc" "$ROOTFS/dev" "$ROOTFS/sys" "$ROOTFS/share" "$ROOTFS/data" "$ROOTFS/local"
    mkdir -p "$DATA_DIR" "$PERSIST_DIR"

    for entry in "share:/share" "proc:/proc" "dev:/dev" "sys:/sys" "data:$PERSIST_DIR" "local:$DATA_DIR"; do
        name=${entry%%:*}
        src=${entry#*:}
        if ! is_mounted "$ROOTFS/$name"; then
            if [ "$name" = "share" ]; then
                bind_share "$ROOTFS/$name" && MOUNTED_BY_US="$name $MOUNTED_BY_US"
            elif mount -o bind "$src" "$ROOTFS/$name"; then
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
            umount_recursive "$ROOTFS/$name"
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
