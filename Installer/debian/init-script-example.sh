#!/bin/bash
#
### BEGIN INIT INFO
# Provides:          duplicati
# Required-Start:
# Required-Stop:
# Default-Start:     2 3 4 5
# Default-Stop:      1
# Short-Description: Duplicati
### END INIT INFO

DAEMON=/usr/bin/duplicati-server
DAEMON_PROCESS=DuplicatiServer
NAME=duplicati
PIDFILE=/var/run/$NAME.pid
DESC="Duplicati backup service"
DEFAULT=/etc/default/$NAME

# Use LSB
. /lib/lsb/init-functions

# If we don't have the basics, don't bother
test -x $DAEMON || exit 0
test -f $DEFAULT && . $DEFAULT        

running_pid()
{
    # Check if a given process pid's cmdline matches a given name
    pid=$1
    name=$2
    [ -z "$pid" ] && return 1
    [ ! -d /proc/$pid ] &&  return 1
    cmd=`cat /proc/$pid/cmdline | tr "\000" "\n"|head -n 1 |cut -d : -f 1`
    # Is this the expected child?
    [ "$cmd" != "$name" ] &&  return 1
    return 0
}

running()
{
# Check if the process is running looking at /proc
# (works for all users)
    # No pidfile, probably no daemon present
    [ ! -f "$PIDFILE" ] && return 1
    # Obtain the pid and check it against the binary name
    pid=`cat $PIDFILE`
    running_pid $pid $DAEMON_PROCESS || return 1
    return 0
}

case "$1" in
    start)
        log_daemon_msg "Starting $DESC" "$NAME"
        if running; then
            log_progress_msg "already running"
            log_end_msg 0
            exit 0
        fi
        rm -f $PIDFILE
        start-stop-daemon --start --quiet --background --make-pidfile --pidfile $PIDFILE --exec $DAEMON -- $DAEMON_OPTS
        log_end_msg $?
        ;;
    stop)
        log_daemon_msg "Stopping $DESC" "$NAME"
        start-stop-daemon --stop --pidfile $PIDFILE
        log_end_msg $?
        ;;
    restart|force-reload)
        $0 stop
        sleep 2
        $0 start
        ;;
    *)
        N=/etc/init.d/$NAME
        echo "Usage: $N {start|stop|restart|force-reload}" >&2
        exit 1
        ;;
esac

exit 0