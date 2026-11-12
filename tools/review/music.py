"""A soft, original lo-fi bed for the review video, synthesised from scratch (no samples, no licences).

    python tools/review/music.py <out.wav> [seconds]

Fmaj7 - Em7 - Dm7 - Cmaj7 at 80 BPM: warm pad, Rhodes-like arpeggios, round bass, brushed drums,
a little vinyl crackle and a big soft room.
"""
import sys
import wave

import numpy as np

SR = 44100
out = sys.argv[1]
length = float(sys.argv[2]) if len(sys.argv) > 2 else 110.0
rng = np.random.default_rng(11)
N = int(SR * (length + 4))
L = np.zeros(N)
R = np.zeros(N)

BPM = 80
beat = 60 / BPM
bar = beat * 4


def hz(m):
    return 440.0 * 2 ** ((m - 69) / 12)


# chords as MIDI notes (voiced low and close)
CHORDS = [
    [53, 57, 60, 64],   # Fmaj7
    [52, 55, 59, 62],   # Em7
    [50, 53, 57, 60],   # Dm7
    [48, 52, 55, 59],   # Cmaj7
]
ALT = [45, 48, 52, 55]  # Am7, every other cycle instead of Cmaj7


def add(sig, start, gain_l, gain_r):
    i = int(start * SR)
    if i >= N:
        return
    j = min(N, i + len(sig))
    L[i:j] += sig[: j - i] * gain_l
    R[i:j] += sig[: j - i] * gain_r


def env_adsr(n, a, d, s, r, hold):
    t = np.arange(n) / SR
    e = np.where(t < a, t / a, s + (1 - s) * np.exp(-(t - a) / d))
    rel_start = hold
    e = np.where(t > rel_start, e * np.exp(-(t - rel_start) / r), e)
    return e


def pad(m, dur):
    n = int((dur + 1.5) * SR)
    t = np.arange(n) / SR
    f = hz(m)
    sig = np.zeros(n)
    for det in (-0.06, 0.0, 0.07):   # gently detuned voices
        ff = f * 2 ** (det / 12)
        sig += np.sin(2 * np.pi * ff * t) + 0.25 * np.sin(2 * np.pi * 2 * ff * t) + 0.08 * np.sin(2 * np.pi * 3 * ff * t)
    return sig / 3 * env_adsr(n, 0.9, 2.0, 0.75, 0.9, dur)


def keys(m, vel):
    n = int(2.4 * SR)
    t = np.arange(n) / SR
    f = hz(m) * (1 + rng.normal(0, 0.0007))
    tone = np.sin(2 * np.pi * f * t) + 0.45 * np.sin(2 * np.pi * 2 * f * t) * np.exp(-t * 6) \
        + 0.12 * np.sin(2 * np.pi * 3 * f * t) * np.exp(-t * 9)
    tremolo = 1 + 0.06 * np.sin(2 * np.pi * 4.2 * t)
    e = np.minimum(t / 0.004, 1) * np.exp(-t * 2.1)
    return tone * e * tremolo * vel


def bass(m, dur):
    n = int((dur + 0.3) * SR)
    t = np.arange(n) / SR
    f = hz(m)
    tone = np.sin(2 * np.pi * f * t) + 0.18 * np.sin(2 * np.pi * 2 * f * t)
    e = np.minimum(t / 0.012, 1) * np.exp(-t * 1.1) * np.where(t > dur, np.exp(-(t - dur) * 18), 1)
    return tone * e


def kick():
    n = int(0.35 * SR)
    t = np.arange(n) / SR
    freq = 48 + 70 * np.exp(-t * 28)
    phase = 2 * np.pi * np.cumsum(freq) / SR
    return np.sin(phase) * np.exp(-t * 9)


def noise_hit(decay, hp):
    n = int(0.25 * SR)
    t = np.arange(n) / SR
    x = rng.normal(0, 1, n)
    for _ in range(hp):          # crude high-pass: differentiate
        x = np.diff(x, prepend=0)
    x /= np.max(np.abs(x)) + 1e-9
    return x * np.exp(-t * decay)


events_kick = []
cycles = int(length / bar) + 2
for b in range(cycles):
    chord = CHORDS[b % 4] if not (b % 8 == 7) else ALT
    t0 = b * bar
    fade = 1.0
    # pad: whole bar, slightly wide
    for k, m in enumerate(chord):
        p = pad(m, bar) * 0.06 * fade
        add(p, t0, 0.8 + 0.2 * (k % 2), 1.0 - 0.2 * (k % 2))
    # bass: beat 1 and the "and" of 3
    root = chord[0] - 12
    add(bass(root, beat * 1.6) * 0.16, t0, 1, 1)
    add(bass(root + (7 if b % 2 else 0), beat * 0.7) * 0.11, t0 + beat * 2.5, 1, 1)
    # Rhodes arpeggio on swung eighths, from bar 2
    if b >= 1:
        pattern = [0, 2, 1, 3, 2, 1, 3, 2] if b % 2 == 0 else [3, 1, 2, 0, 2, 3, 1, 2]
        for i, idx in enumerate(pattern):
            if rng.random() < 0.18:
                continue         # leave some air
            swing = 0.06 * beat if i % 2 else 0
            m = chord[idx] + 12 + (12 if (i == 5 and b % 3 == 0) else 0)
            pan = 0.5 + 0.35 * np.sin(i * 0.9 + b)
            add(keys(m, 0.035 + 0.02 * rng.random()), t0 + i * beat / 2 + swing, 1 - pan * 0.5, 0.5 + pan * 0.5)
    # drums: very soft, from bar 3, out for the last 2 bars
    if 2 <= b < cycles - 3:
        for i in (0, 2):
            add(kick() * 0.22, t0 + i * beat, 1, 1)
            events_kick.append(t0 + i * beat)
        if b % 2 == 1:
            add(kick() * 0.14, t0 + 2.5 * beat, 1, 1)
        for i in (1, 3):
            add(noise_hit(22, 1) * 0.035, t0 + i * beat + 0.01, 0.9, 1)
        for i in range(8):
            swing = 0.06 * beat if i % 2 else 0
            add(noise_hit(70, 2) * (0.012 if i % 2 else 0.018), t0 + i * beat / 2 + swing, 1, 0.8)

# soft "ducking" from the kick, so the bed breathes
duck = np.ones(N)
for k in events_kick:
    i = int(k * SR)
    n = int(0.5 * SR)
    j = min(N, i + n)
    duck[i:j] *= 1 - 0.18 * np.exp(-np.arange(j - i) / SR * 7)
L *= duck
R *= duck

# room: convolve with decaying noise (FFT), stereo decorrelated
ir_len = int(2.6 * SR)
tt = np.arange(ir_len) / SR
ir_l = rng.normal(0, 1, ir_len) * np.exp(-tt * 2.4)
ir_r = rng.normal(0, 1, ir_len) * np.exp(-tt * 2.4)
ir_l /= np.sqrt(np.sum(ir_l ** 2))
ir_r /= np.sqrt(np.sum(ir_r ** 2))
size = 1 << int(np.ceil(np.log2(N + ir_len)))


def conv(x, ir):
    return np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(ir, size), size)[:N]


wet = 0.28
L2 = L + wet * conv(L, ir_l)
R2 = R + wet * conv(R, ir_r)

# warm it up: gentle low-pass (roll off above ~7 kHz) and a touch of tape hiss + crackle


def warm(x):
    X = np.fft.rfft(x, size)
    f = np.fft.rfftfreq(size, 1 / SR)
    X *= 1 / np.sqrt(1 + (f / 7000) ** 4)
    X *= np.where(f < 35, (f / 35) ** 2, 1)
    return np.fft.irfft(X, size)[:N]


L2 = warm(L2)
R2 = warm(R2)
hiss = warm(rng.normal(0, 0.0016, N))
L2 += hiss
R2 += hiss
for _ in range(int(length * 3)):
    i = rng.integers(0, N - 200)
    c = rng.normal(0, 0.02) * np.exp(-np.arange(120) / 18)
    L2[i:i + 120] += c
    R2[i:i + 120] += c * 0.8

# fades, soft clip, normalise
n_len = int(length * SR)
L2 = L2[:n_len]
R2 = R2[:n_len]
fade_in = np.minimum(np.arange(n_len) / (SR * 2.5), 1)
fade_out = np.minimum((n_len - np.arange(n_len)) / (SR * 4.0), 1)
g = fade_in * fade_out
L2 *= g
R2 *= g
peak = max(np.max(np.abs(L2)), np.max(np.abs(R2)))
L2 = np.tanh(L2 / peak * 1.1) * 0.85
R2 = np.tanh(R2 / peak * 1.1) * 0.85

data = np.empty(n_len * 2, dtype=np.int16)
data[0::2] = (L2 * 32767).astype(np.int16)
data[1::2] = (R2 * 32767).astype(np.int16)
with wave.open(out, "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(data.tobytes())
print("ok", out, f"{length:.1f}s")
