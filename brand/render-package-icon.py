# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Render the cairn's rounded rectangles offline, using only the standard library."""

from pathlib import Path
import struct
import xml.etree.ElementTree as ET
import zlib

SIZE = 128
SAMPLES = 8
BACKGROUND = 0x1E
FOREGROUND = 0xEA
BRAND = Path(__file__).resolve().parent


def render():
    svg = ET.parse(BRAND / "cairn-mark.svg").getroot()
    if svg.attrib["viewBox"] != "0 0 64 64":
        raise ValueError("Expected the cairn's 64 by 64 viewBox")
    rectangles = []
    for element in svg:
        if element.tag != "{http://www.w3.org/2000/svg}rect" or set(element.attrib) != {"x", "y", "width", "height", "rx", "fill"} or element.attrib["fill"] != "currentColor":
            raise ValueError("Expected only monochrome rounded rectangles")
        rectangles.append(tuple(float(element.attrib[key]) for key in ("x", "y", "width", "height", "rx")))
    if len(rectangles) != 4:
        raise ValueError("Expected the cairn's four stones")

    def inside(x, y):
        for left, top, width, height, radius in rectangles:
            if left <= x <= left + width and top <= y <= top + height:
                dx = max(left + radius - x, 0, x - (left + width - radius))
                dy = max(top + radius - y, 0, y - (top + height - radius))
                if dx * dx + dy * dy <= radius * radius:
                    return True
        return False

    # Pixel centers on an 8 by 8 subpixel grid; all coordinates are exact binary fractions.
    raw = bytearray()
    for y in range(SIZE):
        raw.append(0)  # PNG filter: none.
        for x in range(SIZE):
            covered = sum(inside((x + (sx + 0.5) / SAMPLES) / 2, (y + (sy + 0.5) / SAMPLES) / 2)
                          for sy in range(SAMPLES) for sx in range(SAMPLES))
            value = BACKGROUND + ((FOREGROUND - BACKGROUND) * covered + SAMPLES * SAMPLES // 2) // (SAMPLES * SAMPLES)
            raw.extend((value, value, value))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    # One stored DEFLATE block makes the bytes independent of compression-library versions.
    compressed = b"\x78\x01\x01" + struct.pack("<HH", len(raw), 0xFFFF - len(raw)) + raw + struct.pack(">I", zlib.adler32(raw))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", compressed) + chunk(b"IEND", b""))


if __name__ == "__main__":
    (BRAND / "package-icon.png").write_bytes(render())
