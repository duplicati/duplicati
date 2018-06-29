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

list_dir "${TRAVIS_BUILD_DIR}"/packages/
echo "travis_fold:start:build_duplicati"
msbuild /p:Configuration=Release Duplicati.sln
cp -r ./Duplicati/Server/webroot ./Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/webroot
echo "travis_fold:end:build_duplicati"

# download and extract testdata
echo "travis_fold:start:download_extract_testdata"
list_dir .

if [ ! -d ~/tmp ]; then mkdir ~/tmp; fi
if [ ! -d ~/download ]; then mkdir ~/download; fi
if [ ! -d ~/download/svn ]; then mkdir ~/download/svn; fi
if [ ! -d ~/download/bulk ]; then mkdir ~/download/bulk; fi

if [ "$CATEGORY" == "SVNData" ] || [ "$CATEGORY" == "SVNDataLong" ]; then
    # test if zip file exists and contains no errors
    unzip -t ~/download/svn/DSMCBE.zip &> /dev/null || \
    wget --progress=dot:giga "https://s3.amazonaws.com/duplicati-test-file-hosting/DSMCBE.zip" -O ~/download/svn/DSMCBE.zip
    list_dir ~/download/svn
fi

if [ "$CATEGORY" == "BulkNormal" ] || [ "$CATEGORY" == "BulkNoSize" ];  then
    # test if zip file exists and contains no errors
    unzip -t ~/download/bulk/data.zip &> /dev/null || \
    wget --progress=dot:giga "https://s3.amazonaws.com/duplicati-test-file-hosting/data.zip" -O ~/download/bulk/data.zip
    list_dir ~/download/bulk
fi

rm -rf ~/duplicati_testdata && mkdir ~/duplicati_testdata

if [ "$CATEGORY" == "SVNData" ] || [ "$CATEGORY" == "SVNDataLong" ]; then
    mkdir ~/duplicati_testdata/DSMCBE
    unzip -q ~/download/svn/DSMCBE.zip -d ~/duplicati_testdata/
    list_dir ~/duplicati_testdata/DSMCBE
fi

if [ "$CATEGORY" == "BulkNormal" ] || [ "$CATEGORY" == "BulkNoSize" ]; then
    mkdir ~/duplicati_testdata/data
    unzip -q ~/download/bulk/data.zip -d ~/duplicati_testdata/
    list_dir ~/duplicati_testdata/data
fi

chown -R $TESTUSER ~/duplicati_testdata/
chmod -R 755 ~/duplicati_testdata
echo "travis_fold:end:download_extract_testdata"

# run unit tests
echo "travis_fold:start:unit_test"
if [[ "$CATEGORY" != "GUI"  && "$CATEGORY" != "" ]]; then
    mono ./testrunner/NUnit.ConsoleRunner.3.5.0/tools/nunit3-console.exe \
    ./Duplicati/UnitTest/bin/Release/Duplicati.UnitTest.dll --where:cat==$CATEGORY --workers=1
fi
echo "travis_fold:end:unit_test"

# start server and run gui tests
echo "travis_fold:start:gui_unit_test"
if [[ "$CATEGORY" == "GUI" ]]; then
    mono ./Duplicati/GUI/Duplicati.GUI.TrayIcon/bin/Release/Duplicati.Server.exe &
    python guiTests/guiTest.py
fi
echo "travis_fold:end:gui_unit_test"
