import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:app/ui/svga.dart';

void main() {
  test('vip6 frame parses: 119 frames @15fps, 400x400', () {
    final m = SvgaMovie.parse(File('assets/svga/vip6_frame.svga').readAsBytesSync());
    expect(m.frameCount, 119);
    expect(m.fps, 15);
    expect(m.viewW, 400);
    expect(m.viewH, 400);
    expect(m.png.length, 119);
    expect(m.perFrame.every((f) => f.isNotEmpty), isTrue);
  });

  test('bugatti gift parses: 121 frames @15fps, 640x1138', () {
    final m = SvgaMovie.parse(File('assets/svga/bugatti_gift.svga').readAsBytesSync());
    expect(m.frameCount, 121);
    expect(m.fps, 15);
    expect(m.viewW, 640);
    expect(m.viewH, 1138);
    expect(m.perFrame.where((f) => f.isNotEmpty).length, greaterThan(100));
  });
}
