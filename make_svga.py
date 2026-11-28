#!/usr/bin/env python3
"""يحوّل فيديو/GIF أو مجلد صور PNG إلى ملف SVGA 2.0 يعمل مع مشغّل VoiceUp.

أمثلة:
  python make_svga.py clip.mp4 out/vip7.svga --kind frame --key green --hole 0.58
  python make_svga.py clip.mp4 out/crown.svga --kind gift --key black
  python make_svga.py frames_dir out/x.svga --kind gift --key none      # PNG بشفافية جاهزة

--kind frame : إطار صورة شخصية (مربع، لوحة 400×400)، --hole يفرّغ دائرة مركزية لتظهر الصورة.
--kind gift  : تأثير هدية بورتريه (لوحة 640×1138).
--key green|#RRGGBB : مفتاح لون (خلفية خضراء مسطحة). black : مفتاح سطوع (للتوهج/النار/الألعاب النارية). none : لا مفتاح.
المتطلبات: Python 3 + Pillow + numpy، و ffmpeg إن كان المدخل فيديو.
"""
import argparse, io, os, shutil, subprocess, sys, tempfile, zlib
import numpy as np
from PIL import Image, ImageDraw

KINDS = {
    # (عرض اللوحة, ارتفاعها, عرض الصورة المخزنة, ارتفاعها)
    'frame': (400, 400, 256, 256),
    'gift': (640, 1138, 320, 569),
}

# ───────── protobuf (كتابة/قراءة بسيطة) ─────────
def wv(n):
    o = bytearray()
    while True:
        b = n & 127; n >>= 7
        if n: o.append(b | 128)
        else:
            o.append(b); return bytes(o)

def rv(b, i):
    r = s = 0
    while True:
        x = b[i]; i += 1; r |= (x & 127) << s; s += 7
        if x < 128: return r, i

def fld(num, wire, val):
    k = wv(num << 3 | wire)
    if wire == 0: return k + wv(val)
    if wire == 2: return k + wv(len(val)) + val
    if wire == 5: return k + val
    raise ValueError(wire)

import struct
def f32(x): return struct.pack('<f', x)

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

# ───────── المفاتيح ─────────
def parse_color(s):
    if s == 'green': return (0, 255, 0)
    s = s.lstrip('#')
    if len(s) != 6: raise SystemExit('لون المفتاح: green أو #RRGGBB')
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))

def apply_key(rgba, key, tol, soft):
    a = rgba.astype(np.float32) / 255.0
    rgb, al = a[..., :3], a[..., 3]
    if key == 'none':
        return rgba
    if key == 'black':
        m = rgb.max(axis=2)
        # قناة ألفا = السطوع؛ اللون يُعاد من الضرب المسبق (توهج إضافي)
        alpha = np.clip(m * 1.15, 0, 1) * al
        out = rgb / np.maximum(m, 1e-4)[..., None]
        out = np.clip(out, 0, 1)
    else:
        kc = np.array(parse_color(key), np.float32) / 255.0
        d = np.sqrt(((rgb - kc) ** 2).sum(axis=2)) / np.sqrt(3.0)
        alpha = np.clip((d - tol) / max(soft, 1e-4), 0, 1) * al
        out = rgb.copy()
        if kc[1] > kc[0] and kc[1] > kc[2]:  # إزالة انعكاس الأخضر على الحواف
            out[..., 1] = np.minimum(out[..., 1], np.maximum(out[..., 0], out[..., 2]))
        elif kc[2] > kc[0] and kc[2] > kc[1]:
            out[..., 2] = np.minimum(out[..., 2], np.maximum(out[..., 0], out[..., 1]))
    res = np.dstack([out, alpha])
    return (np.clip(res, 0, 1) * 255 + 0.5).astype(np.uint8)

def punch_hole(rgba, frac):
    h, w = rgba.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    r = frac * min(w, h) / 2
    dist = np.sqrt((xx - w / 2 + .5) ** 2 + (yy - h / 2 + .5) ** 2)
    k = np.clip((dist - r) / 2.0, 0, 1)  # حافة ناعمة 2px
    rgba = rgba.copy()
    rgba[..., 3] = (rgba[..., 3] * k).astype(np.uint8)
    return rgba

# ───────── المدخل ─────────
def load_frames(src, view_w, view_h, fps, max_frames):
    """يعيد قائمة صور PIL RGBA مقصوصة (cover) على نسبة اللوحة."""
    paths = []
    tmp = None
    if os.path.isdir(src):
        paths = sorted(os.path.join(src, f) for f in os.listdir(src) if f.lower().endswith('.png'))
        if not paths: raise SystemExit('لا توجد صور PNG في المجلد')
        step = 1
    else:
        if not shutil.which('ffmpeg'): raise SystemExit('ffmpeg غير مثبّت')
        tmp = tempfile.mkdtemp()
        vf = f'fps={fps},scale={view_w}:{view_h}:force_original_aspect_ratio=increase,crop={view_w}:{view_h}'
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', src, '-vf', vf, '-frames:v', str(max_frames),
                        os.path.join(tmp, '%04d.png')], check=True)
        paths = sorted(os.path.join(tmp, f) for f in os.listdir(tmp))
        if not paths: raise SystemExit('تعذّر استخراج إطارات من الفيديو')
    paths = paths[:max_frames]
    frames = []
    for p in paths:
        im = Image.open(p).convert('RGBA')
        # قصّ cover إلى نسبة اللوحة
        r = view_w / view_h
        w, h = im.size
        if abs(w / h - r) > 0.01:
            if w / h > r:
                nw = int(h * r); im = im.crop(((w - nw) // 2, 0, (w - nw) // 2 + nw, h))
            else:
                nh = int(w / r); im = im.crop((0, (h - nh) // 2, w, (h - nh) // 2 + nh))
        frames.append(im)
    if tmp: shutil.rmtree(tmp, ignore_errors=True)
    return frames

# ───────── الكتابة ─────────
def build_svga(pngs, view_w, view_h, fps):
    n = len(pngs)
    top = bytearray()
    top += fld(1, 2, b'2.0.0')
    params = fld(1, 5, f32(float(view_w))) + fld(2, 5, f32(float(view_h))) + fld(3, 0, fps) + fld(4, 0, n)
    top += fld(2, 2, params)
    keys = [f'{i:04d}' for i in range(n)]
    for k, png in zip(keys, pngs):
        top += fld(3, 2, fld(1, 2, k.encode()) + fld(2, 2, png))
    layout = fld(1, 5, f32(0.0)) + fld(2, 5, f32(0.0)) + fld(3, 5, f32(float(view_w))) + fld(4, 5, f32(float(view_h)))
    transform = fld(1, 5, f32(1.0)) + fld(4, 5, f32(1.0))
    visible = fld(1, 5, f32(1.0)) + fld(2, 2, layout) + fld(3, 2, transform)
    for i, k in enumerate(keys):
        sp = bytearray(fld(1, 2, k.encode()))
        for j in range(n):
            sp += fld(2, 2, visible if j == i else b'')
        top += fld(4, 2, bytes(sp))
    return zlib.compress(bytes(top), 9)

def verify(data, n, view_w, view_h, fps):
    t = parse(zlib.decompress(data))
    p = dict((f, v) for f, w, v in t if f == 2)
    pf = {f: v for f, w, v in parse(p[2])}
    assert struct.unpack('<f', pf[1])[0] == view_w and struct.unpack('<f', pf[2])[0] == view_h
    assert pf[3] == fps and pf[4] == n
    assert sum(1 for f, w, v in t if f == 3) == n
    sprites = [v for f, w, v in t if f == 4]
    assert len(sprites) == n
    for i, s in enumerate(sprites):
        fr = [v for f, w, v in parse(s) if f == 2]
        assert len(fr) == n
        vis = [j for j, x in enumerate(fr) if x]
        assert vis == [i], 'كل إطار يظهر فيه رسم واحد'

def preview(imgs, path):
    pick = [imgs[int(i * (len(imgs) - 1) / 7)] for i in range(8)] if len(imgs) >= 8 else imgs
    th = []
    for im in pick:
        im = im.copy(); im.thumbnail((220, 300)); th.append(im)
    w = sum(i.width for i in th) + 10 * (len(th) + 1); h = max(i.height for i in th) + 20
    sheet = Image.new('RGBA', (w, h), (110, 110, 110, 255)); d = ImageDraw.Draw(sheet)
    x = 10
    for im in th:
        for yy in range(0, im.height, 16):  # شبكة شطرنج لإظهار الشفافية
            for xx in range(0, im.width, 16):
                if (xx // 16 + yy // 16) % 2 == 0:
                    d.rectangle([x + xx, 10 + yy, x + min(xx + 15, im.width - 1), 10 + min(yy + 15, im.height - 1)], fill=(90, 90, 90, 255))
        sheet.alpha_composite(im, (x, 10)); x += im.width + 10
    sheet.convert('RGB').save(path)

def main():
    ap = argparse.ArgumentParser(description='فيديو/صور → SVGA 2.0 لـ VoiceUp')
    ap.add_argument('input'); ap.add_argument('output')
    ap.add_argument('--kind', choices=KINDS, required=True)
    ap.add_argument('--key', default='none', help='none | black | green | #RRGGBB')
    ap.add_argument('--fps', type=int, default=15)
    ap.add_argument('--max-frames', type=int, default=130)
    ap.add_argument('--tol', type=float, default=0.30, help='تسامح المفتاح اللوني (0-1)')
    ap.add_argument('--soft', type=float, default=0.12, help='نعومة حافة المفتاح')
    ap.add_argument('--hole', type=float, default=0.0, help='نسبة قطر الفتحة المركزية الشفافة للإطارات (مثل 0.58)')
    ap.add_argument('--colors', type=int, default=256, help='عدد ألوان PNG (أقل = أصغر)')
    a = ap.parse_args()

    vw, vh, iw, ih = KINDS[a.kind]
    frames = load_frames(a.input, vw, vh, a.fps, a.max_frames)
    pngs, outs = [], []
    for im in frames:
        arr = apply_key(np.array(im.resize((iw, ih), Image.LANCZOS)), a.key, a.tol, a.soft)
        if a.kind == 'frame' and a.hole > 0:
            arr = punch_hole(arr, a.hole)
        out = Image.fromarray(arr, 'RGBA')
        outs.append(out)
        q = out.quantize(colors=a.colors, method=Image.FASTOCTREE, dither=Image.NONE)
        b = io.BytesIO(); q.save(b, 'PNG', optimize=True); pngs.append(b.getvalue())
    data = build_svga(pngs, vw, vh, a.fps)
    verify(data, len(pngs), vw, vh, a.fps)
    os.makedirs(os.path.dirname(os.path.abspath(a.output)), exist_ok=True)
    open(a.output, 'wb').write(data)
    preview(outs, a.output + '.preview.png')
    mb = len(data) / 1048576
    print(f'تم: {a.output}  إطارات={len(pngs)}  fps={a.fps}  لوحة={vw}x{vh}  الحجم={mb:.2f}MB')
    if mb > 10: print('تحذير: الحجم أكبر من 10MB؛ قلّل --max-frames أو --colors أو --fps')
    if len(pngs) < 8: print('تحذير: عدد الإطارات قليل جداً')

if __name__ == '__main__':
    main()
