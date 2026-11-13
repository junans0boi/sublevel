#!/usr/bin/env python3
"""Synthesises the door sounds (#137, ADR-104): a wooden creak when a door opens, a thud and latch click when it shuts.

Usage: python3 tools/derive_g11_audio.py <project_root>
Needs numpy only. Not AI generated: fixed-seed noise, filters and a swept resonance, like the bullet whiz (S-0005).
"""
import os
import sys
import wave

import numpy as np

SR = 44100


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


def env(n, attack, release):
    t = np.arange(n) / SR
    e = np.minimum(1, t / attack) * np.minimum(1, (n / SR - t) / release)
    return np.clip(e, 0, 1)


def creak(seed):
    # Hinge: stick-slip. A saw whose pitch wanders around 180-420 Hz, chopped into pulses, over filtered noise.
    rng = np.random.default_rng(seed)
    n = int(SR * (0.55 + 0.1 * rng.random()))
    t = np.arange(n) / SR
    pitch = 240 + 110 * np.sin(2 * np.pi * (1.6 + 0.4 * rng.random()) * t) + 90 * t / t[-1]
    phase = np.cumsum(pitch) / SR
    saw = 2 * (phase % 1) - 1
    chop = (np.sin(2 * np.pi * (23 + 6 * rng.random()) * t) > -0.2).astype(float)
    chop = lowpass(chop, 120, 1)
    tone = lowpass(saw, 1400) * chop
    hiss = lowpass(rng.standard_normal(n), 2500) * 0.12
    return (tone + hiss) * env(n, 0.04, 0.2)


def thud(seed):
    # Door into its frame: a low body knock, then the latch click.
    rng = np.random.default_rng(seed)
    n = int(SR * 0.5)
    t = np.arange(n) / SR
    body = np.sin(2 * np.pi * (95 - 30 * t) * t) * np.exp(-t * 16)
    knock = lowpass(rng.standard_normal(n), 700) * np.exp(-t * 28) * 0.8
    click = np.zeros(n)
    k = int(SR * 0.07)
    m = int(SR * 0.012)
    click[k:k + m] = lowpass(rng.standard_normal(m), 5000, 1) * np.exp(-np.arange(m) / (SR * 0.003)) * 0.5
    return body + knock + click


def main(root):
    out = os.path.join(root, "Assets/_Project/Audio/Foley/SynthDoors/Resources/Doors")
    for i in range(3):
        write(os.path.join(out, "open", "door_open_%03d.wav" % i), creak(100 + i), 0.55)
        write(os.path.join(out, "close", "door_close_%03d.wav" % i), thud(200 + i), 0.75)
    print("wrote", out)


if __name__ == "__main__":
    main(sys.argv[1])
