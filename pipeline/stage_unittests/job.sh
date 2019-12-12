#!/bin/bash
. /pipeline/docker-run/markers.sh
. /pipeline/shared/duplicati.sh

function start_test () {
    travis_mark_begin "SETUP NUGET"
    nuget install NUnit.Runners -Version 3.10.0 -OutputDirectory testrunner
    travis_mark_end "SETUP NUGET"

    for CAT in $(echo $testcategories | sed "s/,/ /g")
    do
        travis_mark_begin "UNIT TESTING CATEGORY $CAT"
        mono "${DUPLICATI_ROOT}"/testrunner/NUnit.ConsoleRunner.3.10.0/tools/nunit3-console.exe \
        "${DUPLICATI_ROOT}"/Duplicati/UnitTest/bin/Release/Duplicati.UnitTest.dll --where:cat==$CAT --workers=1
        travis_mark_end "UNIT TESTING CATEGORY $CAT"
    done
}

start_test
