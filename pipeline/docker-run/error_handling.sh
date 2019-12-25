#!/bin/bash

function quit_on_error() {
  local parent_lineno="$1"
  local message="$2"
  local code="${3:-1}"
  if [[ -n "$message" ]] ; then
    echo "Error in $0 line ${parent_lineno}: ${message}; exiting with status ${code}"
  else
    echo "Error in $0 line ${parent_lineno}; exiting with status ${code}"
  fi
  exit "${code}"
}

set -eE
trap 'quit_on_error $LINENO' ERR
set -o pipefail
