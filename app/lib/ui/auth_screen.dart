import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/material.dart';

import '../core/colors.dart';
import 'widgets.dart';

/// دخول/تسجيل بالبريد. التسجيل يكتب نفس بنية Firestore الحالية:
/// users/{uid} + users/{uid}/private/profile + usernames/{name} (18+ إلزامي).
class AuthScreen extends StatefulWidget {
  const AuthScreen({super.key});
  @override
  State<AuthScreen> createState() => _AuthScreenState();
}

class _AuthScreenState extends State<AuthScreen> {
  bool signUp = false, busy = false;
  final email = TextEditingController(), pass = TextEditingController();
  final username = TextEditingController(), display = TextEditingController();
  DateTime? birth;
  String? error;

  @override
  void dispose() {
    for (final c in [email, pass, username, display]) {
      c.dispose();
    }
    super.dispose();
  }

  bool get _is18 {
    if (birth == null) return false;
    final n = DateTime.now();
    var age = n.year - birth!.year;
    if (n.month < birth!.month || (n.month == birth!.month && n.day < birth!.day)) age--;
    return age >= 18;
  }

  String _map(FirebaseAuthException e) {
    switch (e.code) {
      case 'invalid-email': return 'البريد الإلكتروني غير صالح';
      case 'user-not-found':
      case 'wrong-password':
      case 'invalid-credential': return 'البريد أو كلمة المرور غير صحيحة';
      case 'email-already-in-use': return 'هذا البريد مسجّل مسبقاً';
      case 'weak-password': return 'كلمة المرور ضعيفة (6 أحرف على الأقل)';
      case 'operation-not-allowed': return 'تسجيل الدخول بالبريد غير مفعّل في Firebase';
      case 'network-request-failed': return 'تعذّر الاتصال بالإنترنت';
      default: return 'تعذّر الدخول (${e.code})';
    }
  }

  Future<void> _submit() async {
    setState(() { busy = true; error = null; });
    try {
      final auth = FirebaseAuth.instance;
      if (!signUp) {
        await auth.signInWithEmailAndPassword(email: email.text.trim(), password: pass.text);
      } else {
        final u = username.text.trim().toLowerCase();
        if (!RegExp(r'^[a-z0-9_]{3,20}$').hasMatch(u)) throw 'اسم المستخدم: 3-20 حرفاً إنجليزياً صغيراً أو أرقاماً أو _';
        if (display.text.trim().isEmpty || display.text.trim().length > 30) throw 'اسم العرض مطلوب (حتى 30 حرفاً)';
        if (birth == null) throw 'أدخل تاريخ ميلادك';
        if (!_is18) throw 'التطبيق للبالغين 18 سنة فأكثر فقط';
        final cred = await auth.createUserWithEmailAndPassword(email: email.text.trim(), password: pass.text);
        final uid = cred.user!.uid;
        final db = FirebaseFirestore.instance;
        final userRef = db.collection('users').doc(uid);
        final nameRef = db.collection('usernames').doc(u);
        try {
          await db.runTransaction((tx) async {
            if ((await tx.get(nameRef)).exists) throw 'taken';
            tx.set(nameRef, {'uid': uid});
            tx.set(userRef, {
              'username': u,
              'displayName': display.text.trim(),
              'bio': '',
              'createdAt': FieldValue.serverTimestamp(),
            });
            final b = birth!;
            tx.set(userRef.collection('private').doc('profile'), {
              'birthDate': '${b.year.toString().padLeft(4, '0')}-${b.month.toString().padLeft(2, '0')}-${b.day.toString().padLeft(2, '0')}',
            });
          });
        } catch (e) {
          await cred.user!.delete();
          throw e == 'taken' ? 'اسم المستخدم محجوز، اختر غيره' : 'تعذّر إنشاء الملف الشخصي';
        }
      }
    } on FirebaseAuthException catch (e) {
      error = _map(e);
    } catch (e) {
      error = e is String ? e : 'حدث خطأ غير متوقع';
    }
    if (mounted) setState(() => busy = false);
  }

  Widget _field(TextEditingController c, String hint, {bool obscure = false, TextInputType? type}) => Container(
        height: 46,
        margin: const EdgeInsets.only(bottom: 10),
        padding: const EdgeInsets.symmetric(horizontal: 16),
        decoration: BoxDecoration(color: C.surface2, borderRadius: BorderRadius.circular(23)),
        child: TextField(
          controller: c,
          obscureText: obscure,
          keyboardType: type,
          style: plex(14),
          cursorColor: C.amber,
          decoration: InputDecoration(border: InputBorder.none, hintText: hint, hintStyle: plex(14, color: C.muted)),
        ),
      );

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(24, 60, 24, 24),
          child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Text('VoiceUp', textAlign: TextAlign.center, style: kufi(34, w: FontWeight.w700, color: C.amber)),
            const SizedBox(height: 36),
            _field(email, 'البريد الإلكتروني', type: TextInputType.emailAddress),
            _field(pass, 'كلمة المرور', obscure: true),
            if (signUp) ...[
              _field(username, 'اسم المستخدم (a-z و 0-9 و _)'),
              _field(display, 'اسم العرض'),
              GestureDetector(
                onTap: () async {
                  final now = DateTime.now();
                  final d = await showDatePicker(
                    context: context,
                    initialDate: birth ?? DateTime(now.year - 18, now.month, now.day),
                    firstDate: DateTime(1920),
                    lastDate: now,
                  );
                  if (d != null) setState(() => birth = d);
                },
                child: Container(
                  height: 46,
                  margin: const EdgeInsets.only(bottom: 10),
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  alignment: AlignmentDirectional.centerStart,
                  decoration: BoxDecoration(color: C.surface2, borderRadius: BorderRadius.circular(23)),
                  child: Text(
                    birth == null ? 'تاريخ الميلاد (18+)' : '${birth!.year}-${birth!.month.toString().padLeft(2, '0')}-${birth!.day.toString().padLeft(2, '0')}',
                    style: plex(14, color: birth == null ? C.muted : C.cream),
                  ),
                ),
              ),
            ],
            if (error != null)
              Padding(
                padding: const EdgeInsets.only(bottom: 10),
                child: Text(error!, textAlign: TextAlign.center, style: plex(12.5, color: C.red)),
              ),
            GestureDetector(
              onTap: busy ? null : _submit,
              child: Container(
                height: 46,
                alignment: Alignment.center,
                decoration: BoxDecoration(color: C.amber, borderRadius: BorderRadius.circular(23)),
                child: busy
                    ? const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2, color: C.onAmber))
                    : Text(signUp ? 'إنشاء حساب' : 'دخول', style: plex(15, w: FontWeight.w600, color: C.onAmber)),
              ),
            ),
            const SizedBox(height: 14),
            GestureDetector(
              onTap: () => setState(() { signUp = !signUp; error = null; }),
              child: Text(signUp ? 'لدي حساب' : 'حساب جديد', textAlign: TextAlign.center, style: plex(13, color: C.muted)),
            ),
          ]),
        ),
      ),
    );
  }
}
