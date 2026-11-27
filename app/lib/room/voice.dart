import 'package:flutter/foundation.dart';
import 'package:livekit_client/livekit_client.dart' as lk;

/// غلاف رفيع حول LiveKit. لا يغيّر إعدادات الاتصال: الرابط والرمز يأتيان من الخادم.
class VoiceSession extends ChangeNotifier {
  lk.Room? _room;
  bool connected = false;
  String? error;

  Future<void> connect(String url, String token, {required bool canPublish}) async {
    try {
      final room = lk.Room();
      _room = room;
      room.addListener(notifyListeners);
      await room.connect(url, token);
      connected = true;
      if (canPublish) await room.localParticipant?.setMicrophoneEnabled(true);
    } catch (e) {
      error = 'تعذّر الاتصال بالصوت';
    }
    notifyListeners();
  }

  bool get micOn => _room?.localParticipant?.isMicrophoneEnabled() ?? false;

  Future<void> setMic(bool on) async {
    try {
      await _room?.localParticipant?.setMicrophoneEnabled(on);
    } catch (_) {
      error = 'تعذّر تشغيل الميكروفون';
    }
    notifyListeners();
  }

  /// معرّف المتحدثين الآن (identity = uid من رمز الخادم).
  Set<String> get speaking {
    final r = _room;
    if (r == null) return {};
    final s = <String>{};
    for (final p in r.remoteParticipants.values) {
      if (p.isSpeaking) s.add(p.identity);
    }
    final me = r.localParticipant;
    if (me != null && me.isSpeaking) s.add(me.identity);
    return s;
  }

  /// معرّفات من ميكروفونهم مكتوم.
  Set<String> get muted {
    final r = _room;
    if (r == null) return {};
    final s = <String>{};
    for (final p in r.remoteParticipants.values) {
      if (p.isMuted) s.add(p.identity);
    }
    return s;
  }

  Future<void> disconnect() async {
    final r = _room;
    _room = null;
    connected = false;
    try {
      await r?.disconnect();
      await r?.dispose();
    } catch (_) {}
  }
}
