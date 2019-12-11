#!/bin/bash

quit_on_error() {
    echo "Error on line $1, stopping build."
    exit 1
}

list_dir() {
    echo "listing directory $1 :"
    ls -al $1
}

trap 'quit_on_error $LINENO' ERR

CATEGORY=$1
TRAVIS_BUILD_DIR=${2:-.}

if id travis &> /dev/null
then
  TESTUSER=travis
else
  TESTUSER=$(whoami)
fi

echo "Build script starting with parameters TRAVIS_BUILD_DIR=$TRAVIS_BUILD_DIR and CATEGORY=$CATEGORY"

# build duplicati

echo "travis_fold:start:build_duplicati"
dotnet build --configuration Release Duplicati.sln
cp -r ./Duplicati/Server/webroot ./Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/webroot
echo "travis_fold:end:build_duplicati"

rm -rf ~/duplicati_testdata && mkdir ~/duplicati_testdata
chown -R $TESTUSER ~/duplicati_testdata/
chmod -R 755 ~/duplicati_testdata

# run unit tests
echo "travis_fold:start:unit_test"
if [[ "$CATEGORY" != "GUI"  && "$CATEGORY" != "" ]]; then
    echo Running command 'dotnet test ./Duplicati/UnitTest/bin/Release/netcoreapp2.2/Duplicati.UnitTest.dll --no-build --filter Category=$CATEGORY -- NumberOfTestWorkers=1'
    dotnet test ./Duplicati/UnitTest/Duplicati.UnitTest.csproj --no-build --filter Category=Border
fi
echo "travis_fold:end:unit_test"

# start server and run gui tests
echo "travis_fold:start:gui_unit_test"
if [[ "$CATEGORY" == "GUI" ]]; then
    dotnet ./Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/Duplicati.Server.exe &
    python guiTests/guiTest.py
fi
echo "travis_fold:end:gui_unit_test"
