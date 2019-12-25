#!/bin/bash

function travis_mark_begin () {
    echo "travis_fold:start:$1"
    echo "+ START $1"
}

function travis_mark_end () {
    echo "travis_fold:end:$1"
    echo "+ DONE $1"
}
