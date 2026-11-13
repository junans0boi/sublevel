#!/usr/bin/env python3
"""Derives the G4 sound-cue clips (#120, ADR-092) from CC0 recordings, and synthesises the bullet whiz.

Usage: python3 tools/derive_g4_audio.py <mid_dir> <water_dir> <project_root>
  mid_dir:   The Free Firearm Sound Library (CC0) "mid distance" takes, converted with
             `afconvert -f WAVE -d LEI16@44100 -c 1` to pistol.wav (Walther PPQ X_31P), smg.wav (Carl Gustav G_20P),
             rifle.wav (AR-15 D_24P), shotgun.wav (CD H_16P), sniper.wav (Tikka W_24P)
  water_dir: OpenGameArt CC0 splashes: ws.wav (qubodup/ezwa "6 short water splashes", ws.mp3 converted the same way)
             and splash1_0.wav (peludo "Water splash and sand footsteps")
Needs numpy only. Not AI generated: cuts, filters and fixed-seed noise.
"""
import os
import sys
import wave

import numpy as np

SR = 44100


def read(path):
    with wave.open(path) as w:
        assert w.getframerate() == SR and w.getsampwidth() == 2 and w.getnchannels() == 1, path
        return np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32768


def write(path, x, peak=0.7):
    x = x / max(1e-9, np.abs(x).max()) * peak
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype(np.int16).tobytes())


def lowpass(x, hz, passes=2):
    a = 1 - np.exp(-2 * np.pi * hz / SR)
    for _ in range(passes):
        y = np.empty_like(x)
        acc = 0.0
        for i, v in enumerate(x):
            acc += a * (v - acc)
            y[i] = acc
        x = y
    return x


def fade(x, fade_in, fade_out):
    x = x.copy()
    n_in, n_out = int(fade_in * SR), int(fade_out * SR)
    if n_in:
        x[:n_in] *= np.linspace(0, 1, n_in)
    if n_out:
        x[-n_out:] *= np.linspace(1, 0, n_out) ** 2
    return x


def onset(x, threshold=0.2):
    return int(np.argmax(np.abs(x) > threshold * np.abs(x).max()))


def far_shot(x):
    # The mid-distance take from its first peak, 2 s with the field's own echo, highs rolled off into a thump,
    # plus two late low slapbacks (hills) so it reads as "far away" even through the in-game distance low-pass.
    start = max(0, onset(x) - int(0.01 * SR))
    body = x[start:start + 2 * SR]
    body = np.pad(body, (0, 2 * SR - len(body)))
    thump = lowpass(body, 1800)
    out = thump.copy()
    for delay, gain in ((0.33, 0.28), (0.71, 0.16)):
        d = int(delay * SR)
        out[d:] += gain * lowpass(thump, 600)[: len(out) - d]
    return fade(out, 0.002, 0.5)


def whiz(seed):
    # A round passing close: a sharp N-wave snap (supersonic crack) and a short hiss whose pitch falls as it goes by.
    rng = np.random.default_rng(seed)
    n = int(0.32 * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    snap = int(0.0012 * SR)
    out[:snap] = np.linspace(1, -1, snap)
    noise = rng.standard_normal(n)
    centre = 2600 + 1200 * rng.random()
    freq = centre * np.exp(-t * (3 + 2 * rng.random()))
    # Resonant 2-pole band-pass that follows the falling pitch.
    y = np.zeros(n)
    r = 0.985
    for i in range(2, n):
        w = 2 * np.pi * freq[i] / SR
        y[i] = noise[i] * (1 - r) + 2 * r * np.cos(w) * y[i - 1] - r * r * y[i - 2]
    env = np.minimum(t / 0.04, 1) * np.exp(-np.maximum(t - 0.04, 0) / 0.07)
    out += 0.9 * y / max(1e-9, np.abs(y).max()) * env
    return fade(out, 0, 0.05)


def cut(x, a, b):
    return fade(x[int(a * SR):int(b * SR)], 0.003, 0.08)


def main(mid, water, root):
    shots = os.path.join(root, "Assets/_Project/Audio/Weapons/FreeFirearmLibrary/Resources/Shots")
    for name in ("pistol", "smg", "rifle", "shotgun", "sniper"):
        write(os.path.join(shots, f"shot_{name}_far.wav"), far_shot(read(os.path.join(mid, name + ".wav"))))
    synth = os.path.join(root, "Assets/_Project/Audio/Weapons/SynthWhiz/Resources/Shots")
    for i in range(3):
        write(os.path.join(synth, f"whiz_{i:03}.wav"), whiz(9200 + i), peak=0.8)
    steps = os.path.join(root, "Assets/_Project/Audio/Foley/OgaWaterSplash/Resources/Steps/water")
    ws = read(os.path.join(water, "ws.wav"))
    splash = read(os.path.join(water, "splash1_0.wav"))
    for i, clip in enumerate((cut(ws, 0.12, 0.52), cut(ws, 2.70, 2.98), cut(ws, 4.30, 4.58), cut(splash, 0.52, 0.95))):
        write(os.path.join(steps, f"water_{i:03}.wav"), clip)


if __name__ == "__main__":
    main(*sys.argv[1:4])
