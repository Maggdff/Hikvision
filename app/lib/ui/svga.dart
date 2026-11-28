import 'dart:async';
import 'dart:io';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart' show compute;

import '../core/assets.dart';

// ───────────────────────── محلّل SVGA 2.x (protobuf + zlib) ─────────────────────────

class SvgaDraw {
  final String key;
  final double alpha, lx, ly, lw, lh, a, b, c, d, tx, ty;
  SvgaDraw(this.key, this.alpha, this.lx, this.ly, this.lw, this.lh, this.a, this.b, this.c, this.d, this.tx, this.ty);
}

class _Reader {
  final Uint8List b;
  int i = 0;
  _Reader(this.b);
  bool get more => i < b.length;

  int varint() {
    var r = 0, s = 0;
    while (true) {
      final x = b[i++];
      r |= (x & 0x7f) << s;
      s += 7;
      if (x < 0x80) return r;
    }
  }

  int tag() => varint();
  Uint8List bytes() {
    final l = varint();
    final v = Uint8List.sublistView(b, i, i + l);
    i += l;
    return v;
  }

  double f32() {
    final v = ByteData.sublistView(b, i, i + 4).getFloat32(0, Endian.little);
    i += 4;
    return v;
  }

  void skip(int wire) {
    switch (wire) {
      case 0: varint(); break;
      case 1: i += 8; break;
      case 2:
        final n = varint();
        i += n;
        break;
      case 5: i += 4; break;
      default: throw const FormatException('wire type غير مدعوم');
    }
  }
}

/// فيلم SVGA: صور PNG مضغوطة تُفك عند الحاجة فقط، وقائمة رسم لكل إطار.
class SvgaMovie {
  final double viewW, viewH;
  final int fps, frameCount;
  final Map<String, Uint8List> png;
  final List<List<SvgaDraw>> perFrame;
  SvgaMovie._(this.viewW, this.viewH, this.fps, this.frameCount, this.png, this.perFrame);

  Duration get duration => Duration(milliseconds: (frameCount * 1000 / fps).round());

  static final Map<String, Future<SvgaMovie>> _cache = {};

  /// يحمّل الملف من الرابط (قرص ثم شبكة) ويحلّله في isolate منفصل. الإطارات تُخزَّن في الذاكرة؛ الهدايا لا (كبيرة).
  static Future<SvgaMovie> loadUrl(String url, {bool keep = true}) {
    Future<SvgaMovie> run() async {
      final bytes = await RemoteAssets.bytes(url);
      return compute(parse, bytes);
    }

    if (!keep) return run();
    final f = _cache.putIfAbsent(url, run);
    f.then((_) {}, onError: (_) => _cache.remove(url)); // اسمح بإعادة المحاولة لاحقاً
    return f;
  }

  static SvgaMovie parse(Uint8List fileBytes) {
    final raw = Uint8List.fromList(ZLibCodec().decode(fileBytes));
    var vw = 0.0, vh = 0.0;
    var fps = 20, frames = 0;
    final png = <String, Uint8List>{};
    final sprites = <_Sprite>[];

    final r = _Reader(raw);
    while (r.more) {
      final t = r.tag();
      final f = t >> 3, w = t & 7;
      if (f == 2 && w == 2) {
        final p = _Reader(r.bytes());
        while (p.more) {
          final pt = p.tag();
          switch (pt >> 3) {
            case 1: vw = p.f32(); break;
            case 2: vh = p.f32(); break;
            case 3: fps = p.varint(); break;
            case 4: frames = p.varint(); break;
            default: p.skip(pt & 7);
          }
        }
      } else if (f == 3 && w == 2) {
        final e = _Reader(r.bytes());
        String? key;
        Uint8List? val;
        while (e.more) {
          final et = e.tag();
          if ((et >> 3) == 1) {
            key = String.fromCharCodes(e.bytes());
          } else if ((et >> 3) == 2) {
            val = e.bytes();
          } else {
            e.skip(et & 7);
          }
        }
        // نتجاهل غير PNG (مثل الصوت): الملفان المرفقان بلا صوت فعلي
        if (key != null && val != null && val.length > 8 && val[1] == 0x50 && val[2] == 0x4E && val[3] == 0x47) {
          png[key] = val;
        }
      } else if (f == 4 && w == 2) {
        sprites.add(_parseSprite(r.bytes()));
      } else {
        r.skip(w);
      }
    }
    if (vw <= 0 || vh <= 0 || frames <= 0 || fps <= 0) throw const FormatException('SVGA غير صالح');

    final per = List.generate(frames, (_) => <SvgaDraw>[]);
    for (final s in sprites) {
      if (s.key == null || !png.containsKey(s.key)) continue;
      for (var i = 0; i < s.frames.length && i < frames; i++) {
        final fr = s.frames[i];
        if (fr.alpha > 0.001) per[i].add(SvgaDraw(s.key!, fr.alpha, fr.lx, fr.ly, fr.lw, fr.lh, fr.a, fr.b, fr.c, fr.d, fr.tx, fr.ty));
      }
    }
    return SvgaMovie._(vw, vh, fps, frames, png, per);
  }

  static _Sprite _parseSprite(Uint8List data) {
    final s = _Sprite();
    final r = _Reader(data);
    while (r.more) {
      final t = r.tag();
      final f = t >> 3, w = t & 7;
      if (f == 1 && w == 2) {
        s.key = String.fromCharCodes(r.bytes());
      } else if (f == 2 && w == 2) {
        s.frames.add(_parseFrame(r.bytes()));
      } else {
        r.skip(w);
      }
    }
    return s;
  }

  static _FrameData _parseFrame(Uint8List data) {
    final fd = _FrameData();
    final r = _Reader(data);
    while (r.more) {
      final t = r.tag();
      final f = t >> 3, w = t & 7;
      if (f == 1 && w == 5) {
        fd.alpha = r.f32();
      } else if (f == 2 && w == 2) {
        final l = _Reader(r.bytes());
        while (l.more) {
          final lt = l.tag();
          switch (lt >> 3) {
            case 1: fd.lx = l.f32(); break;
            case 2: fd.ly = l.f32(); break;
            case 3: fd.lw = l.f32(); break;
            case 4: fd.lh = l.f32(); break;
            default: l.skip(lt & 7);
          }
        }
      } else if (f == 3 && w == 2) {
        final m = _Reader(r.bytes());
        while (m.more) {
          final mt = m.tag();
          switch (mt >> 3) {
            case 1: fd.a = m.f32(); break;
            case 2: fd.b = m.f32(); break;
            case 3: fd.c = m.f32(); break;
            case 4: fd.d = m.f32(); break;
            case 5: fd.tx = m.f32(); break;
            case 6: fd.ty = m.f32(); break;
            default: m.skip(mt & 7);
          }
        }
      } else {
        r.skip(w);
      }
    }
    return fd;
  }
}

class _Sprite {
  String? key;
  final List<_FrameData> frames = [];
}

class _FrameData {
  double alpha = 0, lx = 0, ly = 0, lw = 0, lh = 0, a = 1, b = 0, c = 0, d = 1, tx = 0, ty = 0;
}

// ───────────────────────── العرض ─────────────────────────

/// تشغيل SVGA بحركته وشفافيته الأصلية: متكرر (إطارات) أو مرة واحدة (هدايا).
/// تُفك صور الإطارات القريبة فقط (نافذة صغيرة) فلا تتضخم الذاكرة.
class SvgaView extends StatefulWidget {
  /// رابط ملف SVGA على CDN.
  final String url;
  final bool loop;
  final BoxFit fit;
  final VoidCallback? onFinished;
  const SvgaView({super.key, required this.url, this.loop = true, this.fit = BoxFit.contain, this.onFinished});

  @override
  State<SvgaView> createState() => _SvgaViewState();
}

class _SvgaViewState extends State<SvgaView> with SingleTickerProviderStateMixin {
  AnimationController? _ctl;
  SvgaMovie? _movie;
  final Map<String, ui.Image> _images = {};
  final Set<String> _loading = {};
  int _lastIndex = -1;
  bool _disposed = false;

  @override
  void initState() {
    super.initState();
    _start();
  }

  Future<void> _start() async {
    try {
      final m = await SvgaMovie.loadUrl(widget.url, keep: widget.loop);
      if (_disposed) return;
      _movie = m;
      final c = AnimationController(vsync: this, duration: m.duration);
      _ctl = c;
      c.addListener(_onTick);
      _ensure(0);
      // نبدأ بعد تجهيز أول إطارات لتفادي وميض البداية
      await Future<void>.delayed(const Duration(milliseconds: 60));
      if (_disposed) return;
      if (widget.loop) {
        c.repeat();
      } else {
        c.addStatusListener((s) {
          if (s == AnimationStatus.completed) widget.onFinished?.call();
        });
        c.forward(from: 0);
      }
      setState(() {});
    } catch (_) {
      // ملف تالف أو نقص ذاكرة: لا نُسقط الشاشة
      widget.onFinished?.call();
    }
  }

  int get _index {
    final m = _movie!;
    return (_ctl!.value * m.frameCount).floor().clamp(0, m.frameCount - 1);
  }

  void _onTick() {
    final i = _index;
    if (i != _lastIndex) {
      _lastIndex = i;
      _ensure(i);
    }
  }

  /// يفك صور الإطارات i..i+10 ويحرّر ما خرج من النافذة.
  void _ensure(int i) {
    final m = _movie!;
    final need = <String>{};
    for (var k = i - 1; k <= i + 10; k++) {
      final idx = widget.loop ? (k % m.frameCount + m.frameCount) % m.frameCount : k;
      if (idx < 0 || idx >= m.frameCount) continue;
      for (final d in m.perFrame[idx]) {
        need.add(d.key);
      }
    }
    for (final k in need) {
      if (_images.containsKey(k) || _loading.contains(k)) continue;
      _loading.add(k);
      _decode(k, m.png[k]!);
    }
    final stale = _images.keys.where((k) => !need.contains(k)).toList();
    for (final k in stale) {
      _images.remove(k)?.dispose();
    }
  }

  Future<void> _decode(String key, Uint8List bytes) async {
    try {
      final codec = await ui.instantiateImageCodec(bytes);
      final frame = await codec.getNextFrame();
      codec.dispose();
      _loading.remove(key);
      if (_disposed) {
        frame.image.dispose();
        return;
      }
      _images[key] = frame.image;
    } catch (_) {
      _loading.remove(key);
    }
  }

  @override
  void dispose() {
    _disposed = true;
    _ctl?.dispose();
    for (final im in _images.values) {
      im.dispose();
    }
    _images.clear();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final c = _ctl, m = _movie;
    if (c == null || m == null) return const SizedBox.shrink();
    return RepaintBoundary(
      child: CustomPaint(
        size: Size.infinite,
        painter: _SvgaPainter(c, m, _images, widget.fit),
      ),
    );
  }
}

class _SvgaPainter extends CustomPainter {
  final AnimationController ctl;
  final SvgaMovie movie;
  final Map<String, ui.Image> images;
  final BoxFit fit;
  _SvgaPainter(this.ctl, this.movie, this.images, this.fit) : super(repaint: ctl);

  @override
  void paint(Canvas canvas, Size size) {
    if (size.isEmpty) return;
    final i = (ctl.value * movie.frameCount).floor().clamp(0, movie.frameCount - 1);
    final sx = size.width / movie.viewW, sy = size.height / movie.viewH;
    final s = fit == BoxFit.cover ? (sx > sy ? sx : sy) : (sx < sy ? sx : sy);
    canvas.save();
    canvas.translate((size.width - movie.viewW * s) / 2, (size.height - movie.viewH * s) / 2);
    canvas.scale(s);
    for (final d in movie.perFrame[i]) {
      final img = images[d.key];
      if (img == null) continue; // لم يُفك بعد: نتخطى بدل الوميض
      canvas.save();
      canvas.transform(Float64List.fromList([d.a, d.b, 0, 0, d.c, d.d, 0, 0, 0, 0, 1, 0, d.tx, d.ty, 0, 1]));
      final paint = Paint()
        ..filterQuality = FilterQuality.medium
        ..color = Color.fromARGB((d.alpha.clamp(0.0, 1.0) * 255).round(), 255, 255, 255);
      canvas.drawImageRect(
        img,
        Rect.fromLTWH(0, 0, img.width.toDouble(), img.height.toDouble()),
        Rect.fromLTWH(d.lx, d.ly, d.lw, d.lh),
        paint,
      );
      canvas.restore();
    }
    canvas.restore();
  }

  @override
  bool shouldRepaint(_SvgaPainter old) => old.movie != movie || old.fit != fit;
}
