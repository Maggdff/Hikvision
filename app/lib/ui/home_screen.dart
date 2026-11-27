import 'package:flutter/material.dart';

import '../core/api.dart';
import '../core/colors.dart';
import '../core/icons.dart';
import '../core/models.dart';
import '../core/profiles.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'room_screen.dart';
import 'widgets.dart';

const _countries = <MapEntry<String, String>>[
  MapEntry('YE', 'اليمن'),
  MapEntry('EG', 'مصر'),
  MapEntry('IQ', 'العراق'),
  MapEntry('AE', 'الإمارات'),
];

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});
  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  int tab = 0; // 0 شعبي، 1 متعلق (المتابَعون)، 2 المجاورون (دولتي)
  String? chip; // رمز الدولة أو null = رائج
  bool searching = false;
  final searchCtl = TextEditingController();
  List<RoomSummary>? rooms;
  List<PromoBanner> banners = const [];
  String? error;
  bool loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    searchCtl.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() { loading = true; error = null; });
    try {
      String path = '/rooms';
      if (tab == 1) {
        path = '/rooms?scope=following';
      } else if (tab == 2) {
        final uid = FirebaseAuth.instance.currentUser!.uid;
        final me = await Profiles.get(uid);
        if (me.country == null) throw ApiException(0, 'no_country');
        path = '/rooms?country=${me.country}';
      } else if (chip != null) {
        path = '/rooms?country=$chip';
      }
      final r = await Api.i.get(path) as Map;
      final list = (r['rooms'] as List).map((e) => RoomSummary.fromJson(e as Map)).toList();
      List<PromoBanner> bs = banners;
      if (tab == 0 && chip == null) {
        try {
          final b = await Api.i.get('/banners') as Map;
          bs = (b['banners'] as List).map((e) => PromoBanner.fromJson(e as Map)).toList();
        } catch (_) {}
      }
      if (!mounted) return;
      setState(() { rooms = list; banners = bs; loading = false; });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        error = e.code == 'no_country' ? 'حدّد دولتك في ملفك الشخصي لعرض غرف المجاورين' : e.message;
        loading = false;
      });
    }
  }

  Widget _topTab(int i, String label) {
    final on = tab == i;
    return GestureDetector(
      onTap: () {
        if (tab == i) return;
        setState(() { tab = i; chip = null; });
        _load();
      },
      child: Container(
        margin: const EdgeInsetsDirectional.only(end: 22),
        padding: const EdgeInsets.only(bottom: 4),
        decoration: on ? const BoxDecoration(border: Border(bottom: BorderSide(color: C.amber, width: 2.5))) : null,
        child: Text(label, style: kufi(on ? 19 : 15, w: on ? FontWeight.w600 : FontWeight.w500, color: on ? C.cream : C.muted)),
      ),
    );
  }

  Widget _chip(String label, bool on, VoidCallback onTap) => GestureDetector(
        onTap: onTap,
        child: Container(
          margin: const EdgeInsetsDirectional.only(end: 8),
          padding: const EdgeInsets.symmetric(horizontal: 15),
          height: 32,
          alignment: Alignment.center,
          decoration: BoxDecoration(color: on ? C.amber : C.surface, borderRadius: BorderRadius.circular(16)),
          child: Text(label, style: plex(13, w: FontWeight.w500, color: on ? C.onAmber : C.muted)),
        ),
      );

  Future<void> _create() async {
    final title = TextEditingController();
    final pass = TextEditingController();
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => Directionality(
        textDirection: TextDirection.rtl,
        child: AlertDialog(
          backgroundColor: C.surface,
          title: Text('غرفة جديدة', style: kufi(17)),
          content: Column(mainAxisSize: MainAxisSize.min, children: [
            TextField(controller: title, maxLength: 40, style: plex(14), decoration: InputDecoration(hintText: 'اسم الغرفة', hintStyle: plex(14, color: C.muted))),
            TextField(controller: pass, obscureText: true, style: plex(14), decoration: InputDecoration(hintText: 'كلمة مرور (اختياري)', hintStyle: plex(14, color: C.muted))),
          ]),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx, false), child: Text('إلغاء', style: plex(14, color: C.muted))),
            TextButton(onPressed: () => Navigator.pop(ctx, true), child: Text('إنشاء', style: plex(14, w: FontWeight.w600, color: C.amber))),
          ],
        ),
      ),
    );
    if (ok != true || title.text.trim().length < 2) return;
    try {
      final r = await Api.i.post('/rooms', body: {
        'title': title.text.trim(),
        if (pass.text.isNotEmpty) 'password': pass.text,
      }) as Map;
      if (!mounted) return;
      final id = r['id'] as String;
      await Navigator.push(context, MaterialPageRoute(builder: (_) => RoomScreen(roomId: id, title: title.text.trim(), password: pass.text.isEmpty ? null : pass.text)));
      _load();
    } on ApiException catch (e) {
      if (mounted) toast(context, e.message);
    }
  }

  @override
  Widget build(BuildContext context) {
    final q = searchCtl.text.trim();
    final shown = (rooms ?? const <RoomSummary>[]).where((r) => q.isEmpty || r.title.contains(q)).toList();
    return SafeArea(
      bottom: false,
      child: Column(children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 18, 16, 0),
          child: searching
              ? Row(children: [
                  Expanded(
                    child: Container(
                      height: 36,
                      padding: const EdgeInsets.symmetric(horizontal: 14),
                      decoration: BoxDecoration(color: C.surface, borderRadius: BorderRadius.circular(18)),
                      child: TextField(
                        controller: searchCtl,
                        autofocus: true,
                        onChanged: (_) => setState(() {}),
                        style: plex(13),
                        cursorColor: C.amber,
                        decoration: InputDecoration(border: InputBorder.none, hintText: 'ابحث عن غرفة', hintStyle: plex(13, color: C.muted)),
                      ),
                    ),
                  ),
                  const SizedBox(width: 12),
                  GestureDetector(
                    onTap: () => setState(() { searching = false; searchCtl.clear(); }),
                    child: Text('إلغاء', style: plex(13, color: C.muted)),
                  ),
                ])
              : Row(children: [
                  _topTab(0, 'شعبي'),
                  _topTab(1, 'متعلق'),
                  _topTab(2, 'المجاورون'),
                  const Spacer(),
                  GestureDetector(onTap: _create, child: const Icn('<path d="M12 5v14M5 12h14"/>', size: 23, color: C.cream)),
                  const SizedBox(width: 14),
                  GestureDetector(onTap: () => setState(() => searching = true), child: const Icn(Ic.search, size: 23, color: C.cream)),
                ]),
        ),
        Expanded(
          child: RefreshIndicator(
            color: C.amber,
            backgroundColor: C.surface,
            onRefresh: _load,
            child: _body(shown),
          ),
        ),
      ]),
    );
  }

  Widget _body(List<RoomSummary> shown) {
    if (loading && rooms == null) return const Loading();
    if (error != null) {
      return ListView(children: [SizedBox(height: 300, child: ErrorBox(message: error!, onRetry: _load))]);
    }
    return ListView(padding: const EdgeInsets.fromLTRB(16, 18, 16, 16), children: [
      if (tab == 0 && banners.isNotEmpty && !searching) _banner(banners.first),
      if (tab == 0 && !searching) ...[
        SizedBox(
          height: 32,
          child: ListView(scrollDirection: Axis.horizontal, children: [
            _chip('رائج', chip == null, () { setState(() => chip = null); _load(); }),
            for (final c in _countries) _chip(c.value, chip == c.key, () { setState(() => chip = c.key); _load(); }),
          ]),
        ),
        const SizedBox(height: 16),
      ],
      if (shown.isEmpty)
        Padding(
          padding: const EdgeInsets.only(top: 80),
          child: Center(child: Text(tab == 1 ? 'لا توجد غرف لمن تتابعهم الآن' : 'لا توجد غرف مفتوحة الآن', style: plex(13, color: C.muted))),
        )
      else
        GridView.builder(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: 2, crossAxisSpacing: 12, mainAxisSpacing: 14, mainAxisExtent: 158 + 8 + 20,
          ),
          itemCount: shown.length,
          itemBuilder: (_, i) => _RoomCard(room: shown[i], onClose: _load),
        ),
    ]);
  }

  Widget _banner(PromoBanner b) => Container(
        height: 132,
        margin: const EdgeInsets.only(bottom: 16),
        clipBehavior: Clip.antiAlias,
        padding: const EdgeInsets.fromLTRB(20, 18, 20, 18),
        decoration: BoxDecoration(color: C.amber, borderRadius: BorderRadius.circular(20)),
        child: Stack(clipBehavior: Clip.none, children: [
          PositionedDirectional(
            bottom: -44, end: -38,
            child: Opacity(opacity: .9, child: const Icn(Ic.crown, size: 120, color: C.onAmber, sw: 1.2)),
          ),
          Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(b.title, style: kufi(24, w: FontWeight.w700, color: C.onAmber, height: 1.35)),
            if (b.subtitle != null) ...[
              const SizedBox(height: 8),
              Text(b.subtitle!, style: plex(12, w: FontWeight.w500, color: C.onAmber)),
            ],
          ]),
        ]),
      );
}

class _RoomCard extends StatelessWidget {
  final RoomSummary room;
  final VoidCallback onClose;
  const _RoomCard({required this.room, required this.onClose});

  @override
  Widget build(BuildContext context) {
    final p = C.paletteFor(room.id);
    final tag = room.isPrivate ? 'خاصة' : (room.topic?.isNotEmpty == true ? room.topic! : 'مباشر');
    final letter = String.fromCharCode(room.title.trim().isEmpty ? 0x645 : room.title.trim().runes.first);
    return GestureDetector(
      onTap: () async {
        String? pw;
        if (room.isPrivate) {
          final c = TextEditingController();
          final ok = await showDialog<bool>(
            context: context,
            builder: (ctx) => Directionality(
              textDirection: TextDirection.rtl,
              child: AlertDialog(
                backgroundColor: C.surface,
                title: Text('غرفة خاصة', style: kufi(17)),
                content: TextField(controller: c, obscureText: true, style: plex(14), decoration: InputDecoration(hintText: 'كلمة المرور', hintStyle: plex(14, color: C.muted))),
                actions: [
                  TextButton(onPressed: () => Navigator.pop(ctx, false), child: Text('إلغاء', style: plex(14, color: C.muted))),
                  TextButton(onPressed: () => Navigator.pop(ctx, true), child: Text('دخول', style: plex(14, w: FontWeight.w600, color: C.amber))),
                ],
              ),
            ),
          );
          if (ok != true) return;
          pw = c.text;
        }
        if (!context.mounted) return;
        await Navigator.push(context, MaterialPageRoute(builder: (_) => RoomScreen(roomId: room.id, title: room.title, password: pw)));
        onClose();
      },
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Container(
          height: 158,
          clipBehavior: Clip.antiAlias,
          decoration: BoxDecoration(color: p[0], borderRadius: BorderRadius.circular(18)),
          child: Stack(children: [
            Center(child: Opacity(opacity: .85, child: Text(letter, style: kufi(44, w: FontWeight.w700, color: p[1])))),
            PositionedDirectional(
              top: 10, start: 10,
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 7),
                height: 20,
                alignment: Alignment.center,
                decoration: BoxDecoration(color: C.ink, borderRadius: BorderRadius.circular(6)),
                child: Text(tag, style: plex(10, w: FontWeight.w600, color: C.amber)),
              ),
            ),
            PositionedDirectional(
              bottom: 10, end: 10,
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 8),
                height: 22,
                decoration: BoxDecoration(color: C.ink.withOpacity(.7), borderRadius: BorderRadius.circular(8)),
                child: Row(mainAxisSize: MainAxisSize.min, children: [
                  const Icn(Ic.bars, size: 12, color: C.amber, sw: 2),
                  const SizedBox(width: 5),
                  Text('${room.seatedCount}', style: plex(11)),
                ]),
              ),
            ),
          ]),
        ),
        const SizedBox(height: 8),
        Text(room.title, maxLines: 1, overflow: TextOverflow.ellipsis, style: plex(13, w: FontWeight.w500)),
      ]),
    );
  }
}
