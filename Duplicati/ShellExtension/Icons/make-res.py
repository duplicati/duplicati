#!/usr/bin/env python3
"""
Builds overlays.res from the overlay .ico files in this folder.

The .res file is embedded into Duplicati.ShellExtension.dll as Win32 icon
resources, so the overlay handlers can point Explorer at the DLL itself
instead of shipping the .ico files next to it. Run this script after
changing any of the icons and commit the resulting overlays.res.

Icon group IDs are assigned in the order below and must match the
IconIndex values in IconOverlayHandler.cs (index = ID - 1).
"""
import struct
from pathlib import Path

ICONS = [
    "overlay_backed_up.ico",  # group ID 1, index 0
    "overlay_warning.ico",    # group ID 2, index 1
    "overlay_error.ico",      # group ID 3, index 2
    "overlay_syncing.ico",    # group ID 4, index 3
]

RT_ICON = 3
RT_GROUP_ICON = 14
MEM_MOVEABLE_DISCARDABLE = 0x1010


def entry(rtype, rid, data):
    """One resource entry: 32 byte header with ordinal type and name, then DWORD-padded data."""
    header = struct.pack("<IIHHHHIHHII", len(data), 32, 0xFFFF, rtype, 0xFFFF, rid, 0, MEM_MOVEABLE_DISCARDABLE, 0, 0, 0)
    return header + data + b"\0" * ((-len(data)) % 4)


def main():
    folder = Path(__file__).resolve().parent
    out = entry(0, 0, b"")  # the empty entry that starts every .res file
    groups = []
    next_icon_id = 1

    for group_id, name in enumerate(ICONS, start=1):
        data = (folder / name).read_bytes()
        _, kind, count = struct.unpack_from("<HHH", data, 0)
        if kind != 1:
            raise SystemExit(f"{name} is not an icon file")

        group = struct.pack("<HHH", 0, 1, count)
        for i in range(count):
            width, height, colors, reserved, planes, bits, size, offset = struct.unpack_from("<BBBBHHII", data, 6 + 16 * i)
            out += entry(RT_ICON, next_icon_id, data[offset:offset + size])
            group += struct.pack("<BBBBHHIH", width, height, colors, reserved, planes, bits, size, next_icon_id)
            next_icon_id += 1
        groups.append((group_id, group))

    for group_id, group in groups:
        out += entry(RT_GROUP_ICON, group_id, group)

    (folder / "overlays.res").write_bytes(out)
    print(f"wrote overlays.res with {next_icon_id - 1} images in {len(ICONS)} icon groups")


if __name__ == "__main__":
    main()
