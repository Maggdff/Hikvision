import 'package:flutter/foundation.dart';

import 'api.dart';

/// كتالوج الهدايا والإطارات بروابط الأصول (من الخادم → CDN). يُحدَّث عند بدء التطبيق وعند ظهور معرّف جديد.
class Catalog {
  static final ValueNotifier<int> rev = ValueNotifier(0);
  static final Map<String, String?> _giftEffect = {};
  static final Map<String, String?> _giftIcon = {};
  static final Map<String, String?> _frame = {};
  static bool _loading = false;

  static Future<void> load() async {
    if (_loading) return;
    _loading = true;
    try {
      final r = await Api.i.get('/catalog') as Map;
      _giftEffect.clear();
      _giftIcon.clear();
      _frame.clear();
      for (final g in (r['gifts'] as List).cast<Map>()) {
        _giftEffect[g['id'] as String] = g['effectUrl'] as String?;
        _giftIcon[g['id'] as String] = g['iconUrl'] as String?;
      }
      for (final f in (r['frames'] as List).cast<Map>()) {
        _frame[f['id'] as String] = f['assetUrl'] as String?;
      }
      rev.value++;
    } catch (_) {
      // بدون كتالوج نعرض الرسوم الافتراضية؛ لا نُسقط الشاشة
    } finally {
      _loading = false;
    }
  }

  static String? effectUrl(String giftId) => _giftEffect[giftId];
  static String? iconUrl(String giftId) => _giftIcon[giftId];
  static String? frameUrl(String frameId) => _frame[frameId];

  /// هدية/إطار غير معروف محلياً (أضافه المشرف للتو): حدّث الكتالوج.
  static Future<void> refreshIfMissing({String? giftId, String? frameId}) async {
    final missing = (giftId != null && !_giftEffect.containsKey(giftId)) ||
        (frameId != null && !_frame.containsKey(frameId));
    if (missing) await load();
  }
}
