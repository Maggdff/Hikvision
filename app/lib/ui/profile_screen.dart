import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/material.dart';

import '../core/api.dart';
import '../core/colors.dart';
import '../core/icons.dart';
import '../core/models.dart';
import 'store_screen.dart';
import 'widgets.dart';

/// شاشة "أنا" بتصميم Majlis الفاتح. تعرض بيانات حقيقية فقط.
class ProfileScreen extends StatefulWidget {
  const ProfileScreen({super.key});
  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  final uid = FirebaseAuth.instance.currentUser!.uid;
  String name = '', username = '';
  int? balance, followers, following;
  LevelInfo? wealth, charm;
  String? frame;
  bool isAdmin = false;
  String? error;
  bool loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() { loading = true; error = null; });
    try {
      final d = await FirebaseFirestore.instance.collection('users').doc(uid).get();
      final m = d.data();
      name = (m?['displayName'] ?? '') as String;
      username = (m?['username'] ?? '') as String;
      final w = await Api.i.get('/wallet') as Map;
      final lv = await Api.i.get('/levels') as Map;
      final so = await Api.i.get('/users/$uid/social') as Map;
      final me = await Api.i.get('/me') as Map;
      if (!mounted) return;
      setState(() {
        balance = (w['balance'] as num).toInt();
        wealth = LevelInfo.fromJson(lv['wealth'] as Map);
        charm = LevelInfo.fromJson(lv['charm'] as Map);
        followers = (so['followers'] as num).toInt();
        following = (so['followingCount'] as num).toInt();
        frame = me['frame'] as String?;
        isAdmin = (me['isAdmin'] ?? false) as bool;
        loading = false;
      });
    } on ApiException catch (e) {
      if (mounted) setState(() { error = e.message; loading = false; });
    } catch (_) {
      if (mounted) setState(() { error = 'تعذّر تحميل الملف الشخصي'; loading = false; });
    }
  }

  /// منح عملات تجريبية لحسابي عبر مسار المنح الإداري الموجود (يمرّ بالدفتر ويتطلب ADMIN_UIDS).
  Future<void> _testCoins() async {
    try {
      final r = await Api.i.post('/admin/grants',
          body: {'uid': uid, 'amount': 50000, 'reason': 'عملات تجريبية للاختبار'}, idemKey: Api.newKey()) as Map;
      if (!mounted) return;
      setState(() => balance = (r['balance'] as num).toInt());
      toast(context, 'تمت إضافة 50,000 عملة تجريبية');
    } on ApiException catch (e) {
      if (mounted) toast(context, e.message);
    }
  }

  Future<void> _signOut() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => Directionality(
        textDirection: TextDirection.rtl,
        child: AlertDialog(
          backgroundColor: C.surface,
          title: Text('تسجيل الخروج', style: kufi(17)),
          content: Text('هل تريد تسجيل الخروج؟', style: plex(14)),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx, false), child: Text('إلغاء', style: plex(14, color: C.muted))),
            TextButton(onPressed: () => Navigator.pop(ctx, true), child: Text('خروج', style: plex(14, w: FontWeight.w600, color: C.amber))),
          ],
        ),
      ),
    );
    if (ok == true) await FirebaseAuth.instance.signOut();
  }

  Color get _ink => C.ink;

  TextStyle _k(double s, {Color? c}) => kufi(s, color: c ?? _ink);
  TextStyle _p(double s, {FontWeight w = FontWeight.w400, Color? c}) => plex(s, w: w, color: c ?? _ink);

  Widget _stat(String v, String label) => Expanded(
        child: Column(children: [
          Text(v, style: _k(20)),
          const SizedBox(height: 3),
          Text(label, style: _p(11.5, c: C.paperMuted)),
        ]),
      );

  Widget _row(String icon, String label, {Widget? trailing, VoidCallback? onTap}) => GestureDetector(
        onTap: onTap,
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
          decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(16), border: Border.all(color: C.paperLine)),
          child: Row(children: [
            Container(
              width: 36, height: 36,
              alignment: Alignment.center,
              decoration: BoxDecoration(color: C.paper, borderRadius: BorderRadius.circular(10)),
              child: Icn(icon, size: 19, color: _ink),
            ),
            const SizedBox(width: 12),
            Expanded(child: Text(label, style: _p(14.5, w: FontWeight.w500))),
            if (trailing != null) ...[trailing, const SizedBox(width: 12)],
            Transform.scale(scaleX: -1, child: const Icn(Ic.chevron, size: 18, color: C.paperChevron)),
          ]),
        ),
      );

  Widget _lvl(int n) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 1),
        decoration: BoxDecoration(color: C.ink, borderRadius: BorderRadius.circular(6)),
        child: Text('$n', style: plex(11, w: FontWeight.w600, color: C.amber)),
      );

  @override
  Widget build(BuildContext context) {
    return Container(
      color: C.paper,
      child: SafeArea(
        bottom: false,
        child: loading
            ? const Loading()
            : error != null
                ? ErrorBox(message: error!, onRetry: _load)
                : RefreshIndicator(
                    color: C.amber,
                    onRefresh: _load,
                    child: ListView(padding: const EdgeInsets.fromLTRB(18, 14, 18, 24), children: [
                      Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
                        GestureDetector(onTap: _signOut, child: Icn(Ic.settings, size: 23, color: _ink)),
                        const SizedBox(width: 23),
                      ]),
                      const SizedBox(height: 28),
                      Row(children: [
                        Padding(padding: const EdgeInsets.all(14), child: Avatar(name: name, key2: uid, size: 84, frame: frame)),
                        const SizedBox(width: 2),
                        Expanded(
                          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                            Text(name, style: _k(21), maxLines: 1, overflow: TextOverflow.ellipsis),
                            const SizedBox(height: 4),
                            Text('@$username', style: _p(12.5, c: C.paperMuted)),
                          ]),
                        ),
                      ]),
                      const SizedBox(height: 28),
                      Row(children: [
                        _stat('${followers ?? 0}', 'المعجبون'),
                        _stat('${following ?? 0}', 'تمت المتابعة'),
                      ]),
                      const SizedBox(height: 28),
                      Row(children: [
                        Expanded(
                          child: Container(
                            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 15),
                            decoration: BoxDecoration(color: C.ink, borderRadius: BorderRadius.circular(18)),
                            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                              Row(children: [Text(groupedNumber(balance ?? 0), style: kufi(24)), const SizedBox(width: 8), const CoinIcon(22)]),
                              const SizedBox(height: 4),
                              Text('رصيدي', style: plex(12, color: C.muted)),
                            ]),
                          ),
                        ),
                        const SizedBox(width: 10),
                        Expanded(
                          child: Container(
                            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 15),
                            decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(18), border: Border.all(color: C.paperLine)),
                            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                              Row(children: [Text(groupedNumber(charm?.points ?? 0), style: _k(24)), const SizedBox(width: 8), DiamondIcon(20, color: _ink)]),
                              const SizedBox(height: 4),
                              Text('الجاذبية', style: _p(12, c: C.paperMuted)),
                            ]),
                          ),
                        ),
                      ]),
                      const SizedBox(height: 18),
                      _row(Ic.star, 'مستوى الثروة', trailing: _lvl(wealth?.level ?? 1)),
                      const SizedBox(height: 8),
                      _row(Ic.crown, 'مستوى الجاذبية', trailing: _lvl(charm?.level ?? 1)),
                      const SizedBox(height: 8),
                      _row(Ic.crown, 'متجر الإطارات', onTap: () async {
                        await Navigator.push(context, MaterialPageRoute(builder: (_) => const StoreScreen()));
                        _load();
                      }),
                      if (isAdmin) ...[
                        const SizedBox(height: 8),
                        _row(Ic.gift, 'عملات تجريبية (50,000)', onTap: _testCoins),
                      ],
                      const SizedBox(height: 8),
                      _row(Ic.power, 'تسجيل الخروج', onTap: _signOut),
                    ]),
                  ),
      ),
    );
  }
}
