import 'package:cloud_firestore/cloud_firestore.dart';

class UserLite {
  final String uid, name;
  final String? country;
  UserLite(this.uid, this.name, [this.country]);
}

/// يجلب اسم العرض من Firestore (users/{uid}، قراءة مسموحة للمسجّلين) مع تخزين مؤقت.
class Profiles {
  static final Map<String, UserLite> _cache = {};

  static Future<UserLite> get(String uid) async {
    final c = _cache[uid];
    if (c != null) return c;
    try {
      final d = await FirebaseFirestore.instance.collection('users').doc(uid).get();
      final m = d.data();
      final name = ((m?['displayName'] ?? m?['username'] ?? '') as String).trim();
      final u = UserLite(uid, name.isEmpty ? 'مستخدم' : name, m?['country'] as String?);
      _cache[uid] = u;
      return u;
    } catch (_) {
      return UserLite(uid, 'مستخدم');
    }
  }

  static UserLite? peek(String uid) => _cache[uid];
}
