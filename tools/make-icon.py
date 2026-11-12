"""Pack the rendered PNGs (DynamicIsland.exe --brand <folder>) into Assets/icon.ico.

    python tools/make-icon.py <brand-folder> Assets/icon.ico
"""
import os
import struct
import sys

folder, out = sys.argv[1], sys.argv[2]
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
blobs = [open(os.path.join(folder, f"icon_{s}.png"), "rb").read() for s in sizes]
header = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
entries = b""
for s, b in zip(sizes, blobs):
    entries += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(b), offset)
    offset += len(b)
open(out, "wb").write(header + entries + b"".join(blobs))
print("ok", out, offset, "bytes")
