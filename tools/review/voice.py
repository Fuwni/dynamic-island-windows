"""Voice-over: one MP3 per segment (Microsoft neural voice via edge-tts) + segments.json with durations.

    python tools/review/voice.py <work-folder> [ffprobe]

Segments whose text didn't change are reused, so re-running after an edit only asks for what changed.
"""
import asyncio
import json
import os
import subprocess
import sys

import edge_tts

here = os.path.dirname(os.path.abspath(__file__))
work = sys.argv[1]
ffprobe = sys.argv[2] if len(sys.argv) > 2 else "ffprobe"
os.makedirs(os.path.join(work, "voice"), exist_ok=True)
spec = json.load(open(os.path.join(here, "narration.json"), encoding="utf-8"))


async def speak(seg):
    path = os.path.join(work, "voice", seg["key"] + ".mp3")
    stamp = path + ".txt"
    wanted = f'{spec["voice"]}|{spec["rate"]}|{seg["text"]}'
    fresh = os.path.exists(path) and os.path.exists(stamp) and open(stamp, encoding="utf-8").read() == wanted
    if not fresh:
        for attempt in range(8):   # the free service drops requests now and then: wait and ask again
            try:
                await edge_tts.Communicate(seg["text"], spec["voice"], rate=spec["rate"]).save(path)
                break
            except edge_tts.exceptions.NoAudioReceived:
                if attempt == 7:
                    raise
                await asyncio.sleep(3 + attempt * 3)
        open(stamp, "w", encoding="utf-8").write(wanted)
        await asyncio.sleep(1.5)
    out = subprocess.run([ffprobe, "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path],
                         capture_output=True, text=True, check=True).stdout.strip()
    return {"key": seg["key"], "title": seg["title"], "sub": seg["sub"], "duration": float(out)}


async def main():
    result = []
    for seg in spec["segments"]:
        result.append(await speak(seg))
        print(f"{seg['key']:8} {result[-1]['duration']:.2f}s", flush=True)
    json.dump({"segments": result}, open(os.path.join(work, "segments.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("total", round(sum(s["duration"] for s in result), 1), "s")

asyncio.run(main())
