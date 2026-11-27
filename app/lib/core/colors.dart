import 'package:flutter/material.dart';

/// ألوان Majlis بالضبط كما في التصميم المعتمد.
class C {
  static const ink = Color(0xFF0F1512);
  static const surface = Color(0xFF17201B);
  static const surface2 = Color(0xFF212C26);
  static const amber = Color(0xFFF4A93B);
  static const onAmber = Color(0xFF2A1A00);
  static const cream = Color(0xFFF4EFE6);
  static const muted = Color(0xFF8FA095);
  static const line = Color(0x17F4EFE6); // rgba(244,239,230,.09)
  static const red = Color(0xFFE5484D);
  // شاشة الملف الشخصي الفاتحة
  static const paper = Color(0xFFF6F1E7);
  static const paperMuted = Color(0xFF5C675F);
  static const paperChevron = Color(0xFF6B766F);
  static const paperLine = Color(0x120F1512); // rgba(15,21,18,.07)

  /// ألوان الأفاتار الدائرية (خلفية، حرف) كما في التصميم.
  static const avatarPalette = <List<Color>>[
    [Color(0xFF2B3A32), Color(0xFFA9C9B5)],
    [Color(0xFF3A3128), Color(0xFFD9BC95)],
    [Color(0xFF2F3440), Color(0xFFA9B6D1)],
    [Color(0xFF3B2F36), Color(0xFFD2AEBF)],
    [Color(0xFF2E3B3B), Color(0xFFA5CFCB)],
    [Color(0xFF38362B), Color(0xFFCFCB9D)],
  ];
  static List<Color> paletteFor(String key) =>
      avatarPalette[key.codeUnits.fold<int>(0, (a, b) => a + b) % avatarPalette.length];
}
