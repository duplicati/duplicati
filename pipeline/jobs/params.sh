#!/bin/bash

export PATH="$PATH:${ROOT_DIR}/pipeline/docker-run"
. error_handling.sh

function add_option () {
  FORWARD_OPTS[${#FORWARD_OPTS[@]}]="$1"
  FORWARD_OPTS[${#FORWARD_OPTS[@]}]="$2"
}

FORWARD_OPTS=()

export FORWARD_OPTS
