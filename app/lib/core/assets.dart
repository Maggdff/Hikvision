import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

/// تنزيل أصول (SVGA/صور) من CDN مع تخزين دائم على القرص (مجلد الكاش للتطبيق).
/// اسم الملف مشتق من الرابط كاملاً؛ والخادم يضيف ?v=<وقت التعديل> فيتجدد الكاش تلقائياً عند تعديل المشرف.
class RemoteAssets {
  static final Map<String, Future<Uint8List>> _inflight = {};
  static const _maxBytes = 30 * 1024 * 1024;

  static Future<Uint8List> bytes(String url) {
    final f = _inflight.putIfAbsent(url, () => _fetch(url));
    f.then((_) {}, onError: (_) => _inflight.remove(url)); // اسمح بإعادة المحاولة لاحقاً
    return f;
  }

  static String _name(String s) {
    var h = 0xcbf29ce484222325;
    for (final c in s.codeUnits) {
      h = (h ^ c) * 0x100000001b3;
    }
    return '${h.toUnsigned(64).toRadixString(16)}_${s.length}';
  }

  static Future<Uint8List> _fetch(String url) async {
    final dir = Directory('${Directory.systemTemp.path}/voiceup_assets');
    if (!dir.existsSync()) dir.createSync(recursive: true);
    final file = File('${dir.path}/${_name(url)}');
    if (file.existsSync() && file.lengthSync() > 0) return file.readAsBytes();

    final client = HttpClient()..connectionTimeout = const Duration(seconds: 15);
    try {
      final req = await client.getUrl(Uri.parse(url)).timeout(const Duration(seconds: 20));
      final res = await req.close().timeout(const Duration(seconds: 30));
      if (res.statusCode != 200) {
        await res.drain<void>();
        throw HttpException('status ${res.statusCode}', uri: Uri.parse(url));
      }
      final out = BytesBuilder(copy: false);
      await for (final chunk in res.timeout(const Duration(seconds: 60))) {
        out.add(chunk);
        if (out.length > _maxBytes) throw const HttpException('asset too large');
      }
      final data = out.takeBytes();
      final tmp = File('${file.path}.part');
      await tmp.writeAsBytes(data, flush: true);
      await tmp.rename(file.path);
      return data;
    } finally {
      client.close();
    }
  }
}
