#!/bin/bash
. /pipeline/docker-run/markers.sh
. /pipeline/shared/duplicati.sh

# The version of mono available within the selenium docker image is too old so
# we will install the latest version.
function update_mono () {
	sudo apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF
	echo "deb https://download.mono-project.com/repo/ubuntu stable-bionic main" | sudo tee /etc/apt/sources.list.d/mono-official-stable.list
	sudo apt update
	sudo apt install -y mono-complete
}

function start_test () {
    pip install selenium
    pip install --upgrade urllib3

    # wget "https://github.com/mozilla/geckodriver/releases/download/v0.23.0/geckodriver-v0.23.0-linux32.tar.gz"
    # tar -xvzf geckodriver*
    # chmod +x geckodriver
    # export PATH=$PATH:/duplicati/

    #echo -n | openssl s_client -connect scan.coverity.com:443 | sed -ne '/-BEGIN CERTIFICATE-/,/-END CERTIFICATE-/p' | tee -a /etc/ssl/certs/ca-
    mono "${DUPLICATI_ROOT}/Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/Duplicati.Server.exe" &
    cd
    python "${DUPLICATI_ROOT}/guiTests/guiTest.py"
}

travis_mark_begin "INTEGRATION TESTING"
update_mono
start_test
travis_mark_end "INTEGRATION TESTING"
