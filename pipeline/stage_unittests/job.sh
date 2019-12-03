#!/bin/bash
. /pipeline/docker-run/markers.sh
. /pipeline/shared/duplicati.sh

function get_and_extract_test_zip () {
    travis_mark_begin "DOWNLOADING TEST DATA $CAT"
    # test if zip file exists and contains no errors, otherwise redownload
    unzip -t ~/download/"${CAT}"/"${testdata}" &> /dev/null || \
    wget --progress=dot:giga "https://s3.amazonaws.com/duplicati-test-file-hosting/${testdata}" -O ~/download/"${CAT}"/"${testdata}"
    unzip -q ~/download/"${CAT}"/"${testdata}" -d ${UNITTEST_BASEFOLDER}
    travis_mark_end "DOWNLOADING TEST DATA $CAT"
}

function start_test () {
    nuget install NUnit.Runners -Version 3.10.0 -OutputDirectory testrunner

    for CAT in $(echo $testcategories | sed "s/,/ /g")
    do
        mkdir -p ~/download/"${CAT}"
        export UNITTEST_BASEFOLDER=~/duplicati_testdata/"${CAT}"
        rm -rf ${UNITTEST_BASEFOLDER} && mkdir -p ${UNITTEST_BASEFOLDER}

        if [[ ${testdata} != "" ]]; then
            get_and_extract_test_zip
        fi

        travis_mark_begin "UNIT TESTING CATEGORY $CAT"
        mono "${DUPLICATI_ROOT}"/testrunner/NUnit.ConsoleRunner.3.10.0/tools/nunit3-console.exe \
        "${DUPLICATI_ROOT}"/Duplicati/UnitTest/bin/Release/Duplicati.UnitTest.dll --where:cat==$CAT --workers=1
        travis_mark_end "UNIT TESTING CATEGORY $CAT"
    done
}

travis_mark_begin "UNIT TESTING"
start_test
travis_mark_end "UNIT TESTING"
