This folder is only used during development.
It is used to place the sqlite.dll inside the Mac app bundle so the hosted instance can start.
It is not copied to the resulting bundle, and it is only dynamically loaded, so it is not a problem if it is outdated, but may give weird debugging errors.
