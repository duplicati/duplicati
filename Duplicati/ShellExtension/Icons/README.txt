Duplicati Shell Extension Overlay Icons
=======================================

This folder should contain the following icon files:

1. overlay_backed_up.ico - Green checkmark overlay for successfully backed up folders
2. overlay_warning.ico - Yellow/orange warning overlay for folders with backup warnings
3. overlay_error.ico - Red X overlay for folders with backup failures
4. overlay_syncing.ico - Blue/circular arrows overlay for folders with backup in progress

Icon Requirements:
- Format: ICO file with multiple resolutions (16x16, 32x32, 48x48, 256x256)
- Should have transparent background
- Should be visually similar to Windows cloud sync overlays (OneDrive, Dropbox, etc.)

The icons should be placed in this directory for the shell extension to work properly.
These icons are loaded at runtime by the COM shell extension handlers.
