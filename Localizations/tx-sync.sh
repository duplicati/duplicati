#!/bin/bash
cd $( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )

REPO_ROOT=$(git rev-parse --show-toplevel)

if ! git diff-index --quiet HEAD -- || [ -n "$(git ls-files --others --exclude-standard)" ]; then
    echo "Aborting: there are uncommitted changes in the working tree."
    echo "Please commit or discard them before running this script."
    exit 1
fi

./extract_all.sh
./push_source_files_to_transifex.sh

git -C "$REPO_ROOT" stash push -m "tx-sync: discard extract/push artifacts"
git -C "$REPO_ROOT" stash drop

./pull_from_transifex.sh
./compile_all.sh
