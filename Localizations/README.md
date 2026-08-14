# Localization flow

The localization is handled via [Transifex](https://transifex.com/duplicati) and all text work should go through Transifex.

Please do not modify the `.po`/`.mo` files found here.

# Updating Transifex strings

When the codebase changes, the strings can be extracted and sent to Transifex by running:

```bash
./extract_all.sh
./push_source_files_to_transifex.sh
```

This will update all information in Transifex and let translators know what has changed and what is missing.
Note that this is a messy process that will cause line-ending changes in most files, so it is best done when there are no pending git changes on the local copy.

After running the process, simply discard the changes.

# Updating the .mo/.po files

When new work has been performed in Transifex, this can be pulled and applied to the source:

```bash
./pull_from_transifex.sh
./compile_all.sh
```

This will change the local files have the new changes.
After inspecting the changes, this can be used to make a PR with updates.

# Full sync

To perform the full sync (extract, push, pull, compile) in one step, run:

```bash
./tx-sync.sh
```

This script will abort if there are any uncommitted changes in the working tree.
It runs `extract_all.sh` and `push_source_files_to_transifex.sh`, then discards the resulting artifacts with `git stash` before pulling and compiling the latest translations.
