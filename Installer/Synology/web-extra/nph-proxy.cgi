#!/bin/sh

# Basic configuration for the proxy handler
export SYNO_LOGIN_CGI=/usr/syno/synoman/webman/login.cgi
export SYNO_AUTHENTICATE_CGI=/usr/syno/synoman/webman/modules/authenticate.cgi
export SYNO_ALL_USERS=0
export SYNO_AUTO_XSRF=1
export SYNO_SKIP_AUTH=0
export PROXY_HOST=localhost
export PROXY_PORT=8200
export PROXY_DEBUG=0
export PROXY_LOGFILE=/var/log/duplicati-proxy.log

# It seems it is faster to set this up in the script,
# instead of letting the CGIProxyHandler do it
if [ "z$SYNO_SKIP_AUTH" != "z1" ]; then

    if [ "z$HTTP_X_SYNO_TOKEN" == "z" ]; then
        if [ "z$SYNO_AUTO_XSRF" == "z1" ]; then
            TOKEN=`$SYNO_LOGIN_CGI < /dev/null | grep SynoToken | cut -d '"' -f 4`
            export HTTP_X_SYNO_TOKEN="$TOKEN"
        fi
    fi

    if [ "z$HTTP_X_SYNO_TOKEN" != "z" ]; then
        USERNAME=`QUERY_STRING=SynoToken=$HTTP_X_SYNO_TOKEN $SYNO_AUTHENTICATE_CGI < /dev/null`
        export SYNO_USERNAME="$USERNAME"
    fi

    if [ "z$USERNAME" != "z" ]; then
        GROUP_IDS=`id -G "$USERNAME" < /dev/null`
        export SYNO_GROUP_IDS="$GROUP_IDS"
    fi
fi

# This line is injected by the postinst script
#mono /var/packages/Duplicati/target/CGIProxyHandler.exe