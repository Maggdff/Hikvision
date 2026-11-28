import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:app/ui/svga.dart';

void main() {
  // ملفات SVGA لم تعد في المشروع (مستضافة خارجياً)؛ يُتخطى الاختبار إن لم تكن موجودة محلياً
  final hasFixtures = File('test/fixtures/vip6_frame.svga').existsSync();

  test('vip6 frame parses: 119 frames @15fps, 400x400', skip: !hasFixtures, () {
    final m = SvgaMovie.parse(File('test/fixtures/vip6_frame.svga').readAsBytesSync());
    expect(m.frameCount, 119);
    expect(m.fps, 15);
    expect(m.viewW, 400);
    expect(m.viewH, 400);
    expect(m.png.length, 119);
    expect(m.perFrame.every((f) => f.isNotEmpty), isTrue);
  });

  test('bugatti gift parses: 121 frames @15fps, 640x1138', skip: !hasFixtures, () {
    final m = SvgaMovie.parse(File('test/fixtures/bugatti_gift.svga').readAsBytesSync());
    expect(m.frameCount, 121);
    expect(m.fps, 15);
    expect(m.viewW, 640);
    expect(m.viewH, 1138);
    expect(m.perFrame.where((f) => f.isNotEmpty).length, greaterThan(100));
  });
}
