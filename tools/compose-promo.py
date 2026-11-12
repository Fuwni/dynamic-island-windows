"""Put the promo together: background + filmed stage (rounded) + timed, sliding captions -> 1080x1920 MP4.

    DynamicIsland.exe --promo <folder> ru
    python tools/compose-promo.py <folder> <out.mp4> [path-to-ffmpeg]
"""
import json
import os
import subprocess
import sys

folder, out = sys.argv[1], sys.argv[2]
ffmpeg = sys.argv[3] if len(sys.argv) > 3 else "ffmpeg"
meta = json.load(open(os.path.join(folder, "captions.json"), encoding="utf-8"))
caps = meta["captions"]
total = meta["total"]

STAGE_X, STAGE_Y, CAP_Y = 40, 640, 268

args = [ffmpeg, "-y", "-loglevel", "error",
        "-loop", "1", "-framerate", "60", "-i", os.path.join(folder, "bg.png"),
        "-i", os.path.join(folder, "stage.mp4"),
        "-loop", "1", "-framerate", "60", "-i", os.path.join(folder, "mask.png")]
for c in caps:
    args += ["-loop", "1", "-framerate", "60", "-i", os.path.join(folder, f"cap_{c['key']}.png")]

f = []
f.append("[1:v]format=rgba[sv]")
f.append("[2:v]format=gray[m]")
f.append("[sv][m]alphamerge[st]")
f.append(f"[0:v][st]overlay={STAGE_X}:{STAGE_Y}:shortest=1[b0]")
for i, c in enumerate(caps):
    t0 = c["t"]
    t1 = caps[i + 1]["t"] if i + 1 < len(caps) else total + 1
    # fade + slide up when it appears, quick fade out before the next one
    f.append(f"[{3 + i}:v]format=rgba,fade=t=in:st={t0:.3f}:d=0.35:alpha=1,"
             f"fade=t=out:st={max(t0, t1 - 0.18):.3f}:d=0.18:alpha=1[c{i}]")
    y = f"{CAP_Y}+44*pow(max(0\\,1-(t-{t0:.3f})/0.4)\\,3)"
    f.append(f"[b{i}][c{i}]overlay=0:'{y}':enable='between(t,{t0:.3f},{t1:.3f})'[b{i + 1}]")

script = os.path.join(folder, "filter.txt")
open(script, "w", encoding="utf-8").write(";\n".join(f))
args += ["-/filter_complex", script, "-map", f"[b{len(caps)}]",
         "-t", f"{total:.2f}", "-r", "60", "-c:v", "libx264", "-preset", "slow", "-crf", "16",
         "-pix_fmt", "yuv420p", "-profile:v", "high", "-movflags", "+faststart", out]
subprocess.run(args, check=True)
print("ok", out)
