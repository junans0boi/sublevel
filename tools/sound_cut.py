#!/usr/bin/env python3
"""Cut one-shot gun sounds out of staged Freesound files (ADR-250). No Unity, no ffmpeg: macOS afconvert + stdlib.

  python3 -I tools/sound_cut.py <staging_dir> <out_dir> [spec.json]

Spec (tools/sound_shots.json): {"out": {"<name>": {"src": "<freesound id>", "t0": s, "t1": s, "peak": 0.9,
  "lp": hz|null, "loop_xf": s (seamless loop), "dir": subfolder of out_dir, "fade_in": s, "fade_out": s, "delay": s, "gain": 1.0}}}. Output: 16-bit 44.1 kHz mono WAV.
Staged files are not in git (AssetStaging/); only the cut results and this spec are.
"""
import glob, json, math, os, struct, subprocess, sys, tempfile

SR = 44100


def read_wav(path):
    b = open(path, 'rb').read()
    i = 12
    while i < len(b):
        cid, sz = b[i:i + 4], struct.unpack('<I', b[i + 4:i + 8])[0]
        if cid == b'data':
            d = b[i + 8:i + 8 + sz]
            n = len(d) // 2
            return [x / 32768.0 for x in struct.unpack('<%dh' % n, d[:n * 2])]
        i += 8 + sz + (sz & 1)
    raise ValueError('no data chunk: ' + path)


def write_wav(path, s):
    pcm = struct.pack('<%dh' % len(s), *[max(-32768, min(32767, int(round(x * 32767)))) for x in s])
    hdr = b'RIFF' + struct.pack('<I', 36 + len(pcm)) + b'WAVEfmt ' + struct.pack('<IHHIIHH', 16, 1, 1, SR, SR * 2, 2, 16)
    open(path, 'wb').write(hdr + b'data' + struct.pack('<I', len(pcm)) + pcm)


def load_source(staging, sid, cache):
    if sid not in cache:
        src = sorted(glob.glob(os.path.join(staging, sid + '__*')))
        src = [p for p in src if not p.endswith('.m4a')]
        if not src:
            raise FileNotFoundError('staged source ' + sid)
        with tempfile.TemporaryDirectory() as t:
            out = os.path.join(t, 'x.wav')
            subprocess.run(['afconvert', '-f', 'WAVE', '-d', 'LEI16@%d' % SR, '-c', '1', src[0], out], check=True)
            cache[sid] = read_wav(out)
    return cache[sid]


def lowpass(s, hz):
    a = 1 - math.exp(-2 * math.pi * hz / SR)
    y, o = 0.0, []
    for x in s:
        y += a * (x - y)
        o.append(y)
    return o


def render(src, c):
    s = src[int(c['t0'] * SR):int(c['t1'] * SR)]
    if c.get('lp'):
        s = lowpass(s, c['lp'])
    s = [0.0] * int(c.get('delay', 0) * SR) + s
    n = len(s)
    fi, fo = int(c.get('fade_in', 0.002) * SR), int(c.get('fade_out', 0.05) * SR)
    if c.get('loop_xf'):
        fi = fo = 0
    for i in range(min(fi, n)):
        s[i] *= i / fi
    for i in range(min(fo, n)):
        s[n - 1 - i] *= i / fo
    xf = int(c.get('loop_xf', 0) * SR)
    if xf:  # seamless loop: the tail fades into the head (equal power), then is dropped
        n -= xf
        s = [s[i] * math.sin(0.5 * math.pi * i / xf) + s[n + i] * math.cos(0.5 * math.pi * i / xf) if i < xf else s[i] for i in range(n)]
    pk = max(abs(x) for x in s) or 1.0
    k = c.get('peak', 0.9) / pk * c.get('gain', 1.0)
    return [x * k for x in s]


def main(staging, out_dir, spec_path):
    spec = json.load(open(spec_path))
    os.makedirs(out_dir, exist_ok=True)
    cache = {}
    for name, c in spec['out'].items():
        s = render(load_source(staging, c['src'], cache), c)
        d = os.path.join(out_dir, c.get('dir', ''))
        os.makedirs(d, exist_ok=True)
        write_wav(os.path.join(d, name + '.wav'), s)
        print('%-28s %6.2fs peak %.2f' % (name, len(s) / SR, max(abs(x) for x in s)))


if __name__ == '__main__':
    here = os.path.dirname(os.path.abspath(__file__))
    main(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else os.path.join(here, 'sound_shots.json'))
