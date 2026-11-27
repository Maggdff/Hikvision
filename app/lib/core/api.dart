import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:firebase_auth/firebase_auth.dart';

import 'config.dart';

class ApiException implements Exception {
  final int status;
  final String code;
  ApiException(this.status, this.code);

  /// رسالة عربية واضحة للمستخدم.
  String get message {
    switch (code) {
      case 'insufficient_funds': return 'رصيدك لا يكفي لإرسال هذه الهدية';
      case 'unauthenticated': return 'انتهت الجلسة، سجّل الدخول مجدداً';
      case 'room_full': return 'الغرفة ممتلئة';
      case 'wrong_password': return 'كلمة مرور الغرفة غير صحيحة';
      case 'banned': return 'تم حظرك من هذه الغرفة';
      case 'network': return 'تعذّر الاتصال بالخادم، تحقق من الإنترنت';
      case 'not_configured': return 'الخادم غير معدّ في هذا الإصدار';
      default: return 'حدث خطأ ($code)';
    }
  }

  @override
  String toString() => 'ApiException($status, $code)';
}

class Api {
  Api._();
  static final Api i = Api._();
  final _client = HttpClient()..connectionTimeout = const Duration(seconds: 15);

  Future<Map<String, String>> _headers({String? idemKey}) async {
    final user = FirebaseAuth.instance.currentUser;
    if (user == null) throw ApiException(401, 'unauthenticated');
    final token = await user.getIdToken();
    return {
      'Authorization': 'Bearer $token',
      'Content-Type': 'application/json',
      if (idemKey != null) 'Idempotency-Key': idemKey,
    };
  }

  Uri _uri(String path) {
    if (!Config.apiConfigured) throw ApiException(0, 'not_configured');
    final base = Config.apiUrl.replaceAll(RegExp(r'/+$'), '');
    return Uri.parse('$base/v1$path');
  }

  Future<dynamic> _send(String method, String path, {Object? body, String? idemKey}) async {
    try {
      final req = await _client.openUrl(method, _uri(path)).timeout(const Duration(seconds: 20));
      (await _headers(idemKey: idemKey)).forEach(req.headers.set);
      if (body != null) req.add(utf8.encode(jsonEncode(body)));
      final res = await req.close().timeout(const Duration(seconds: 20));
      final text = await res.transform(utf8.decoder).join();
      if (res.statusCode >= 200 && res.statusCode < 300) {
        if (text.isEmpty) return null;
        return jsonDecode(text);
      }
      var code = 'http_${res.statusCode}';
      try {
        final j = jsonDecode(text);
        if (j is Map && j['error'] is String) code = j['error'] as String;
        if (j is Map && j['message'] is String && j['error'] == null) code = j['message'] as String;
      } catch (_) {}
      throw ApiException(res.statusCode, code);
    } on ApiException {
      rethrow;
    } catch (_) {
      throw ApiException(0, 'network');
    }
  }

  Future<dynamic> get(String path) => _send('GET', path);
  Future<dynamic> post(String path, {Object? body, String? idemKey}) =>
      _send('POST', path, body: body ?? const {}, idemKey: idemKey);
  Future<dynamic> put(String path, {Object? body}) => _send('PUT', path, body: body);
  Future<dynamic> delete(String path) => _send('DELETE', path);

  static String newKey() {
    final r = Random.secure();
    const chars = 'abcdefghijklmnopqrstuvwxyz0123456789';
    return List.generate(32, (_) => chars[r.nextInt(chars.length)]).join();
  }
}
