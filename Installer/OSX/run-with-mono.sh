#!/bin/bash
#
# This is a stub script that allows .apps to be relocatable on OSX but still
# find the managed assembly.
#
# This is copied from the Mono macpack tool and modified to fit Duplicati 
#
# The Mono Version Check is from here:
#   http://mjhutchinson.com/journal/2010/01/24/creating_mac_app_bundle_for_gtk_app
#

# Figure out the full path to this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
if [ "z${SCRIPT_DIR}" == "z" ]; then
	SCRIPT_DIR="$( dirname "$0" )"
fi

APP_PATH="$( dirname "${SCRIPT_DIR}" )"

export GDIPLUS_NOX=1

#mono version check

REQUIRED_MAJOR=2
REQUIRED_MINOR=8

VERSION_TITLE="Cannot launch $APP_NAME"
VERSION_MSG="$APP_NAME requires the Mono Framework version $REQUIRED_MAJOR.$REQUIRED_MINOR or later."
DOWNLOAD_URL="http://www.go-mono.com/mono-downloads/download.html"

# Try system default mono if an override was not supplied
if [ "z${MONO_BIN}" == "z" ]; then
	MONO_BIN=`which mono`

	# Check if there was no mono found
	if [ "z$MONO_BIN" == "z" ]
	then
		# Check if there is a HomeBrew install of mono instead
		MONO_VERSION="$(/usr/local/bin/mono --version | grep 'Mono JIT compiler version ' |  cut -f5 -d\ )"
		if [ -f "/usr/local/bin/mono" ]
		then
			MONO_BIN="/usr/local/bin/mono"
		fi
	fi
fi

MONO_VERSION="$(${MONO_BIN} --version | grep 'Mono JIT compiler version ' |  cut -f5 -d\ )"
MONO_VERSION_MAJOR="$(echo $MONO_VERSION | cut -f1 -d.)"
MONO_VERSION_MINOR="$(echo $MONO_VERSION | cut -f2 -d.)"
if [ -z "$MONO_VERSION" ] \
	|| [ $MONO_VERSION_MAJOR -lt $REQUIRED_MAJOR ] \
	|| [ $MONO_VERSION_MAJOR -eq $REQUIRED_MAJOR -a $MONO_VERSION_MINOR -lt $REQUIRED_MINOR ] 
then
	osascript \
	-e "set question to display dialog \"$VERSION_MSG\" with title \"$VERSION_TITLE\" buttons {\"Cancel\", \"Download...\"} default button 2" \
	-e "if button returned of question is equal to \"Download...\" then open location \"$DOWNLOAD_URL\""
	echo "$VERSION_TITLE"
	echo "$VERSION_MSG"
	exit 1
fi

# Move into the folder where all the assemblies are located
cd "${APP_PATH}/Resources"

# Make a symlink so Duplicati shows up as "Duplicati" in ps and not as "mono"
if [ ! -d "./bin" ]; then mkdir bin ; fi

if [ ! -d "./bin" ]
then
	# We cannot make the extra dir (most likely we are running on read-only medium)
	# Instead we attempt to use exec to set the appname
	OSX_VERSION=$(uname -r | cut -f1 -d.)
	if [ $OSX_VERSION -lt 9 ]; then  # If OSX version is 10.4
		exec "${MONO_BIN}" "$ASSEMBLY" $@
	else
		exec -a "$APP_NAME" "${MONO_BIN}" "$ASSEMBLY" $@
	fi		
else

	# We can make the helper file, lets use that
	if [ -f "./bin/$APP_NAME" ]; then rm -f "./bin/$APP_NAME" ; fi
	ln -s "${MONO_BIN}" "./bin/$APP_NAME"

	# Start Duplicati using the renamed symlink to Mono
	"./bin/$APP_NAME" "$ASSEMBLY" $@
fi