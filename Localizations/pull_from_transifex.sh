#!/bin/bash
# transifex client in PATH necessary
cd $( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )
tx pull -use-git-timestamps --force --mode onlytranslated --minimum-perc 10
