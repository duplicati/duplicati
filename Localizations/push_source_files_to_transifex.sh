#!/bin/bash
# transifex client in PATH necessary
cd "$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )" || exit 1
tx push --source
