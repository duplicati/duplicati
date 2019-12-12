#!/bin/bash

export PATH="$PATH:${ROOT_DIR}/pipeline/docker-run"
. error_handling.sh

which ts > /dev/null
if [[ $? -ne 0 ]]; then
  echo "please install ts. e.g. brew install moreutils/apt-get install moreutils"
  exit 1
fi