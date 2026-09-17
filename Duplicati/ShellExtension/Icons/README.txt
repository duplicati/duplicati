Duplicati Shell Extension Overlay Icons
=======================================

This folder holds the icons for the folder status overlays. They reuse the
tray icon designs so the states read the same in Explorer and in the tray:

1. overlay_backed_up.ico - Black tray icon (inactive) for successfully backed up folders
2. overlay_warning.ico - Orange tray icon with exclamation mark for folders with backup warnings
3. overlay_error.ico - Red tray icon with cross for folders with backup failures
4. overlay_syncing.ico - Green tray icon with play symbol for folders with backup in progress

The .ico files are generated from the tray icon SVGs in Assets/tray icons by
make-icons.py (needs rsvg-convert), so every size is rendered from the vector
source. The icons are not shipped as files. They are compiled into overlays.res by
make-res.py and embedded into Duplicati.ShellExtension.dll as Win32 icon
resources, so the overlay handlers point Explorer at the DLL itself.

After changing a tray icon SVG, run:

    python3 make-icons.py
    python3 make-res.py

and commit the updated overlays.res. The order of the icons in make-res.py
defines the icon index used by each handler in IconOverlayHandler.cs.
