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

# Discard the extract/push artifacts, but only drop what was actually stashed:
# "git stash push" saves nothing (and still exits 0) when there are no changes,
# and an unconditional drop would then delete a pre-existing stash entry.
STASH_BEFORE=$(git -C "$REPO_ROOT" rev-parse -q --verify refs/stash || true)
git -C "$REPO_ROOT" stash push -m "tx-sync: discard extract/push artifacts"
STASH_AFTER=$(git -C "$REPO_ROOT" rev-parse -q --verify refs/stash || true)
if [ "$STASH_AFTER" != "$STASH_BEFORE" ]; then
    git -C "$REPO_ROOT" stash drop
fi

./pull_from_transifex.sh
./compile_all.sh
