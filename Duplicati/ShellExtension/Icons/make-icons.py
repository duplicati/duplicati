#!/usr/bin/env python3
"""
Renders the overlay .ico files from the tray icon SVGs in Assets/tray icons.

Every size is rasterized from the vector source with rsvg-convert, so the
small sizes stay crisp instead of being scaled-down bitmaps, and the badge is
placed in the bottom-left corner at every size (see BADGE_FRACTION). The .ico files
hold 32-bit images for the small sizes and a PNG image for 256 pixels.
Run make-res.py afterwards to rebuild overlays.res.

Requires rsvg-convert (librsvg) on the PATH.
"""
import struct
import subprocess
import zlib
from pathlib import Path

SIZES = [16, 20, 24, 32, 40, 48, 64, 256]

# Explorer draws an overlay image at the size of the item icon, choosing the
# entry that matches, and in some views additionally shrinks it. Like the
# shortcut arrow, every entry therefore holds a reduced badge in the
# bottom-left corner of an otherwise transparent canvas, so the badge keeps the
# same proportion no matter which entry Explorer picks.
BADGE_FRACTION = 0.6

SOURCES = {
    "overlay_backed_up": "tray_icons_inactive.svg",
    "overlay_warning": "tray_icons_warning.svg",
    "overlay_error": "tray_icons_error.svg",
    "overlay_syncing": "tray_icons_running.svg",
}


def render_svg(svg, size):
    return subprocess.run(
        ["rsvg-convert", "-w", str(size), "-h", str(size), str(svg)],
        check=True, capture_output=True).stdout


def decode_png(data):
    """Minimal decoder for the 8-bit RGBA PNGs rsvg-convert produces."""
    pos, idat, width, height = 8, b"", 0, 0
    while pos < len(data):
        length, = struct.unpack_from(">I", data, pos)
        kind = data[pos + 4:pos + 8]
        chunk = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if kind == b"IHDR":
            width, height, depth, colortype = struct.unpack_from(">IIBB", chunk, 0)
            if depth != 8 or colortype != 6:
                raise SystemExit("expected an 8-bit RGBA PNG from rsvg-convert")
        elif kind == b"IDAT":
            idat += chunk

    raw = zlib.decompress(idat)
    stride = width * 4
    rows, prev = [], bytearray(stride)
    for y in range(height):
        start = y * (stride + 1)
        filt = raw[start]
        line = bytearray(raw[start + 1:start + 1 + stride])
        for i in range(stride):
            a = line[i - 4] if i >= 4 else 0
            b = prev[i]
            c = prev[i - 4] if i >= 4 else 0
            if filt == 1:
                line[i] = (line[i] + a) & 255
            elif filt == 2:
                line[i] = (line[i] + b) & 255
            elif filt == 3:
                line[i] = (line[i] + (a + b) // 2) & 255
            elif filt == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(bytes(line))
        prev = line
    return width, height, rows


def png_bytes(width, height, rows):
    raw = b"".join(b"\0" + row for row in rows)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def place_in_corner(size, badge, rows):
    """Puts a badge x badge image bottom-left on a transparent size x size canvas."""
    empty = bytes(size * 4)
    top = [empty] * (size - badge)
    tail = bytes((size - badge) * 4)
    return size, size, top + [row + tail for row in rows]


def bmp_entry(width, height, rows):
    """32-bit BGRA bottom-up DIB with an empty AND mask, as stored inside .ico files."""
    header = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = b"".join(
        bytes((row[x * 4 + 2], row[x * 4 + 1], row[x * 4], row[x * 4 + 3]))
        for row in reversed(rows) for x in range(width))
    mask_stride = ((width + 31) // 32) * 4
    return header + pixels + b"\0" * (mask_stride * height)


def write_ico(path, images):
    entries, blobs = b"", b""
    offset = 6 + 16 * len(images)
    for size, data in images:
        entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset + len(blobs))
        blobs += data
    path.write_bytes(struct.pack("<HHH", 0, 1, len(images)) + entries + blobs)


def main():
    folder = Path(__file__).resolve().parent
    assets = folder.parents[2] / "Assets" / "tray icons"
    for name, svg in SOURCES.items():
        images = []
        for size in SIZES:
            badge = max(8, round(size * BADGE_FRACTION))
            _, _, rows = decode_png(render_svg(assets / svg, badge))
            canvas = place_in_corner(size, badge, rows)
            images.append((size, png_bytes(*canvas) if size >= 256 else bmp_entry(*canvas)))
        write_ico(folder / f"{name}.ico", images)
        print(f"wrote {name}.ico from {svg}")


if __name__ == "__main__":
    main()
