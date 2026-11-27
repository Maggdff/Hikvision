#!/usr/bin/env python3
"""يصغّر صور إطارات ملف SVGA 2.x دون المساس بالحركة (sprites/transforms) ولا الشفافية.
المشغّل يرسم كل صورة داخل مستطيل layout الأصلي، لذا تصغير البكسلات آمن بصرياً.
الاستخدام: optimize_svga.py in.svga out.svga SCALE_OR_WxH"""
import io, sys, zlib
from PIL import Image

def rv(b, i):
    r = s = 0
    while True:
        x = b[i]; i += 1; r |= (x & 127) << s; s += 7
        if x < 128: return r, i

def wv(n):
    o = bytearray()
    while True:
        b = n & 127; n >>= 7
        if n: o.append(b | 128)
        else: o.append(b); return bytes(o)

def parse(b):
    i = 0; out = []
    while i < len(b):
        k, i = rv(b, i); f, w = k >> 3, k & 7
        if w == 0: v, i = rv(b, i)
        elif w == 2:
            l, i = rv(b, i); v = b[i:i + l]; i += l
        elif w == 5: v = b[i:i + 4]; i += 4
        elif w == 1: v = b[i:i + 8]; i += 8
        else: raise ValueError(w)
        out.append((f, w, v))
    return out

def enc(fields):
    o = bytearray()
    for f, w, v in fields:
        o += wv(f << 3 | w)
        if w == 0: o += wv(v)
        elif w == 2: o += wv(len(v)) + v
        else: o += v
    return bytes(o)

def shrink(png, target):
    im = Image.open(io.BytesIO(png)).convert('RGBA')
    size = target if isinstance(target, tuple) else (max(1, round(im.width * target)), max(1, round(im.height * target)))
    im = im.resize(size, Image.LANCZOS)
    im = im.quantize(colors=256, method=Image.FASTOCTREE, dither=Image.NONE)
    b = io.BytesIO(); im.save(b, 'PNG', optimize=True)
    return b.getvalue()

def main(src, dst, spec):
    target = tuple(int(x) for x in spec.split('x')) if 'x' in spec else float(spec)
    top = parse(zlib.decompress(open(src, 'rb').read()))
    res = []
    for f, w, v in top:
        if f == 3:
            kv = parse(v); nk = []
            for ff, ww, vv in kv:
                if ff == 2 and vv[:4] == b'\x89PNG': vv = shrink(vv, target)
                nk.append((ff, ww, vv))
            v = enc(nk)
        res.append((f, w, v))
    out = zlib.compress(enc(res), 9)
    open(dst, 'wb').write(out)
    print(f'{src}: {len(open(src,"rb").read())/1e6:.2f}MB -> {dst}: {len(out)/1e6:.2f}MB')

if __name__ == '__main__':
    main(*sys.argv[1:4])
