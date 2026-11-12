"""Assemble the narrated review video.

    python tools/review/compose.py <work> <brand> <out.mp4> [ffmpeg]

<work>  : segments.json + voice/*.mp3 (voice.py), music.wav (music.py),
          stage.mp4 + timeline.json + bg.png + mask.png + cap_*.png (DynamicIsland.exe --review <work>)
<brand> : intro/ and outro/ frame folders (DynamicIsland.exe --brand <brand>)
"""
import json
import os
import subprocess
import sys
import wave

import numpy as np

work, brand, out = sys.argv[1], sys.argv[2], sys.argv[3]
ffmpeg = sys.argv[4] if len(sys.argv) > 4 else "ffmpeg"
P = lambda *a: os.path.join(work, *a)
FPS = 60
XF = 0.5  # cross-fade between intro / tour / outro


def run(args):
    subprocess.run([ffmpeg, "-y", "-loglevel", "error"] + args, check=True)


def frames_to_mp4(folder, path):
    run(["-framerate", str(FPS), "-i", os.path.join(folder, "%04d.png"),
         "-c:v", "libx264", "-preset", "slow", "-crf", "15", "-pix_fmt", "yuv420p", path])
    return len(os.listdir(folder)) / FPS


# ---------------------------------------------------------------- 1. intro / outro animations
intro_len = frames_to_mp4(os.path.join(brand, "intro"), P("intro.mp4"))
outro_len = frames_to_mp4(os.path.join(brand, "outro"), P("outro.mp4"))

# ---------------------------------------------------------------- 2. the tour: background + stage + captions
tl = json.load(open(P("timeline.json"), encoding="utf-8"))
caps, total = tl["segments"], tl["total"]
args = ["-loop", "1", "-framerate", str(FPS), "-i", P("bg.png"), "-i", P("stage.mp4"),
        "-loop", "1", "-framerate", str(FPS), "-i", P("mask.png")]
for c in caps:
    args += ["-loop", "1", "-framerate", str(FPS), "-i", P(f"cap_{c['key']}.png")]
f = ["[1:v]format=rgba[sv]", "[2:v]format=gray[m]", "[sv][m]alphamerge[st]", "[0:v][st]overlay=40:640:shortest=1[b0]"]
for i, c in enumerate(caps):
    t0 = c["t"]
    t1 = caps[i + 1]["t"] if i + 1 < len(caps) else total + 1
    f.append(f"[{3 + i}:v]format=rgba,fade=t=in:st={t0:.3f}:d=0.35:alpha=1,"
             f"fade=t=out:st={max(t0, t1 - 0.2):.3f}:d=0.2:alpha=1[c{i}]")
    y = f"268+44*pow(max(0\\,1-(t-{t0:.3f})/0.4)\\,3)"
    f.append(f"[b{i}][c{i}]overlay=0:'{y}':enable='between(t,{t0:.3f},{t1:.3f})'[b{i + 1}]")
# a thin progress line under the stage
f.append(f"color=c=white@0.12:s=1000x6:r={FPS},format=rgba[track]")
f.append(f"color=c=0xF0A27A:s=1000x6:r={FPS},format=rgba[bar]")
f.append(f"[track][bar]overlay=x='-1000+1000*t/{total:.3f}':y=0[prog]")   # clipped to the track
f.append(f"[b{len(caps)}][prog]overlay=x=40:y=1530[tour]")
open(P("filter_tour.txt"), "w", encoding="utf-8").write(";\n".join(f))
run(args + ["-/filter_complex", P("filter_tour.txt"), "-map", "[tour]", "-t", f"{total:.3f}", "-r", str(FPS),
            "-c:v", "libx264", "-preset", "slow", "-crf", "15", "-pix_fmt", "yuv420p", P("tour.mp4")])

# ---------------------------------------------------------------- 3. a soft whoosh for scene changes
SR = 48000
n = int(0.5 * SR)
t = np.arange(n) / SR
rng = np.random.default_rng(3)
noise = rng.normal(0, 1, n)
spec = np.fft.rfft(noise)
freqs = np.fft.rfftfreq(n, 1 / SR)
spec *= np.exp(-((freqs - 2200) / 1800) ** 2)
sw = np.fft.irfft(spec, n)
sw *= np.sin(np.pi * np.minimum(t / 0.5, 1)) ** 2
sw /= np.max(np.abs(sw))
sw = (sw * 0.5 * 32767).astype(np.int16)
with wave.open(P("whoosh.wav"), "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(sw.tobytes())

# ---------------------------------------------------------------- 4. final: picture + voice + music
o1 = intro_len - XF                   # tour starts here
o2 = o1 + total - XF                  # outro starts here
length = o2 + outro_len

voice = [("intro", 0.7)] + [(c["key"], o1 + c["t"] + 0.12) for c in caps] + [("cta", o2 + 1.0)]
inputs = ["-i", P("intro.mp4"), "-i", P("tour.mp4"), "-i", P("outro.mp4"), "-i", P("music.wav")]
for key, _ in voice:
    inputs += ["-i", P("voice", key + ".mp3")]
whooshes = [o1 + c["t"] - 0.2 for c in caps[1:]] + [o2 - 0.1]
for _ in whooshes:
    inputs += ["-i", P("whoosh.wav")]

g = [f"[0:v][1:v]xfade=transition=fade:duration={XF}:offset={o1:.3f}[v01]",
     f"[v01][2:v]xfade=transition=fade:duration={XF}:offset={o2:.3f},format=yuv420p[vout]"]
vi = []
for i, (key, at) in enumerate(voice):
    ms = int(at * 1000)
    g.append(f"[{4 + i}:a]aformat=sample_rates=48000:channel_layouts=stereo,"
             f"highpass=f=80,equalizer=f=3200:t=q:w=1.2:g=2.5,acompressor=threshold=-20dB:ratio=3:attack=6:release=90,"
             f"adelay={ms}|{ms}[vo{i}]")
    vi.append(f"[vo{i}]")
g.append(f"{''.join(vi)}amix=inputs={len(vi)}:normalize=0,apad[vox]")
g.append("[vox]asplit=2[vox1][vox2]")
wi = []
base = 4 + len(voice)
for j, at in enumerate(whooshes):
    ms = int(max(0, at) * 1000)
    g.append(f"[{base + j}:a]aformat=sample_rates=48000:channel_layouts=stereo,volume=0.16,adelay={ms}|{ms}[w{j}]")
    wi.append(f"[w{j}]")
g.append(f"{''.join(wi)}amix=inputs={len(wi)}:normalize=0,apad[wh]")
g.append(f"[3:a]aformat=sample_rates=48000:channel_layouts=stereo,volume=0.30,"
         f"afade=t=out:st={length - 3:.2f}:d=3[mus]")
g.append("[mus][vox2]sidechaincompress=threshold=0.025:ratio=5:attack=30:release=450:makeup=1[duck]")
g.append(f"[vox1][duck][wh]amix=inputs=3:normalize=0,atrim=0:{length:.3f},loudnorm=I=-14:TP=-1.5:LRA=9[aout]")
open(P("filter_final.txt"), "w", encoding="utf-8").write(";\n".join(g))
run(inputs + ["-/filter_complex", P("filter_final.txt"), "-map", "[vout]", "-map", "[aout]",
              "-c:v", "libx264", "-preset", "slow", "-crf", "17", "-profile:v", "high", "-r", str(FPS),
              "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-movflags", "+faststart", "-t", f"{length:.3f}", out])
print(f"ok {out}  {length:.1f}s")
