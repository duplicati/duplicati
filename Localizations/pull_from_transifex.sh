#!/bin/bash
# transifex client in PATH necessary
cd $( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )
tx pull --use-git-timestamps --languages=de,fr,es,zh_CN,nl_NL,pl,fi,ru,da,it,zh_TW,cs,pt_BR,sr_RS,zh_HK,pt,lt,lv,sk_SK,ro,sv_SE,th,hu,sk,ca,ja_JP,bn,ko,fr_CA,en_GB
