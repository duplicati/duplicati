#!/bin/bash
# transifex client in PATH necessary
cd $( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )
tx pull --language=de,fr,es,zh_CN,nl_NL,pl,fi,ru
