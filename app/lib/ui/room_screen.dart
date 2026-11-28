import 'dart:async';

import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/material.dart';

import '../core/api.dart';
import '../core/catalog.dart';
import '../core/colors.dart';
import '../core/icons.dart';
import '../core/models.dart';
import '../core/profiles.dart';
import '../room/voice.dart';
import 'gift_sheet.dart';
import 'widgets.dart';

class RoomScreen extends StatefulWidget {
  final String roomId;
  final String title;
  final String? password;
  const RoomScreen({super.key, required this.roomId, required this.title, this.password});
  @override
  State<RoomScreen> createState() => _RoomScreenState();
}

class _RoomScreenState extends State<RoomScreen> {
  final voice = VoiceSession();
  final me = FirebaseAuth.instance.currentUser!.uid;
  RoomState? state;
  String? error;
  bool joining = true, following = false;
  final List<ChatMessage> messages = [];
  int lastMsgId = 0;
  Timer? stateTimer, msgTimer, clock;
  final input = TextEditingController();
  final scroll = ScrollController();
  Gift? effect;
  String? effectBy;

  @override
  void initState() {
    super.initState();
    voice.addListener(() => mounted ? setState(() {}) : null);
    _join();
  }

  @override
  void dispose() {
    stateTimer?.cancel();
    msgTimer?.cancel();
    clock?.cancel();
    voice.disconnect();
    voice.dispose();
    input.dispose();
    scroll.dispose();
    super.dispose();
  }

  Future<void> _join() async {
    setState(() { joining = true; error = null; });
    try {
      final j = JoinInfo.fromJson(await Api.i.post('/rooms/${widget.roomId}/join', body: {
        if (widget.password != null) 'password': widget.password,
      }) as Map);
      await _refreshState();
      if (!mounted) return;
      setState(() => joining = false);
      voice.connect(j.url, j.token, canPublish: j.role != 'listener');
      stateTimer = Timer.periodic(const Duration(seconds: 4), (_) => _refreshState());
      msgTimer = Timer.periodic(const Duration(seconds: 2), (_) => _pollMessages());
      clock = Timer.periodic(const Duration(seconds: 1), (_) => mounted ? setState(() {}) : null);
      _pollMessages();
      _loadFollow();
    } on ApiException catch (e) {
      if (mounted) setState(() { error = e.message; joining = false; });
    }
  }

  Future<void> _refreshState() async {
    try {
      final s = RoomState.fromJson(await Api.i.get('/rooms/${widget.roomId}') as Map);
      for (final seat in s.seats) {
        Profiles.get(seat.uid).then((_) => mounted ? setState(() {}) : null);
        if (seat.frame != null) Catalog.refreshIfMissing(frameId: seat.frame);
      }
      Profiles.get(s.ownerUid).then((_) => mounted ? setState(() {}) : null);
      if (mounted) setState(() => state = s);
    } on ApiException catch (e) {
      if (e.status == 404 || e.status == 403) {
        if (mounted) {
          toast(context, 'أُغلقت الغرفة');
          Navigator.pop(context);
        }
      }
    }
  }

  Future<void> _loadFollow() async {
    final s = state;
    if (s == null || s.ownerUid == me) return;
    try {
      final r = await Api.i.get('/users/${s.ownerUid}/social') as Map;
      if (mounted) setState(() => following = (r['iFollow'] ?? false) as bool);
    } catch (_) {}
  }

  Future<void> _toggleFollow() async {
    final s = state;
    if (s == null) return;
    try {
      if (following) {
        await Api.i.delete('/follows/${s.ownerUid}');
      } else {
        await Api.i.put('/follows/${s.ownerUid}');
      }
      if (mounted) setState(() => following = !following);
    } on ApiException catch (e) {
      if (mounted) toast(context, e.message);
    }
  }

  Future<void> _pollMessages() async {
    try {
      final r = await Api.i.get('/rooms/${widget.roomId}/messages?after=$lastMsgId') as Map;
      final fresh = (r['messages'] as List).map((e) => ChatMessage.fromJson(e as Map)).toList();
      if (fresh.isEmpty || !mounted) return;
      final firstLoad = lastMsgId == 0;
      for (final m in fresh) {
        Profiles.get(m.uid);
        if (m.id > lastMsgId) lastMsgId = m.id;
        if (m.kind == 'gift' && !firstLoad) {
          final gid = m.meta?['giftId'] as String?;
          if (gid != null && m.uid != me) {
            await Catalog.refreshIfMissing(giftId: gid);
            _playEffect(Gift.fromJson({'id': gid, 'nameAr': m.meta?['nameAr'] ?? '', 'tier': _tierOf((m.meta?['priceCoins'] ?? 0) as int), 'priceCoins': m.meta?['priceCoins'] ?? 0}), m.uid);
          }
        }
      }
      setState(() => messages.addAll(fresh));
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (scroll.hasClients) scroll.jumpTo(scroll.position.maxScrollExtent);
      });
    } catch (_) {}
  }

  String _tierOf(int price) => price >= 1000 ? 'large' : price >= 200 ? 'medium' : 'small';

  void _playEffect(Gift g, String by) {
    if (effect != null) return;
    setState(() { effect = g; effectBy = by; });
  }

  Future<void> _sendText() async {
    final t = input.text.trim();
    if (t.isEmpty) return;
    input.clear();
    try {
      await Api.i.post('/rooms/${widget.roomId}/messages', body: {'body': t});
      _pollMessages();
    } on ApiException catch (e) {
      if (mounted) toast(context, e.message);
    }
  }

  Future<void> _seatTap(int i) async {
    final s = state;
    if (s == null) return;
    final occupant = s.seatAt(i);
    if (occupant == null) {
      if (s.mySeat != null) return;
      try {
        await Api.i.post('/rooms/${widget.roomId}/seat-requests');
        if (mounted) toast(context, 'أُرسل طلب الصعود، بانتظار موافقة المضيف');
      } on ApiException catch (e) {
        if (mounted) toast(context, e.message);
      }
    } else if (occupant.uid == me) {
      await voice.setMic(!voice.micOn);
    }
  }

  void _openGifts() {
    final s = state;
    if (s == null) return;
    showModalBottomSheet(
      context: context,
      backgroundColor: Colors.transparent,
      barrierColor: Colors.black.withOpacity(.45),
      isScrollControlled: true,
      builder: (_) => Directionality(
        textDirection: TextDirection.rtl,
        child: GiftSheet(
          roomId: widget.roomId,
          myUid: me,
          seats: s.seats,
          onSent: (g) {
            _playEffect(g, me);
            _pollMessages();
          },
        ),
      ),
    );
  }

  void _openMenu() {
    final s = state;
    if (s == null) return;
    showModalBottomSheet(
      context: context,
      backgroundColor: C.surface,
      shape: const RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(22))),
      builder: (ctx) => Directionality(
        textDirection: TextDirection.rtl,
        child: _MenuSheet(roomId: widget.roomId, state: s, onChanged: () { _refreshState(); }, onClosed: () { Navigator.pop(ctx); Navigator.pop(context); }),
      ),
    );
  }

  String _elapsed() {
    final c = state?.createdAt;
    if (c == null) return '00:00:00';
    final d = DateTime.now().toUtc().difference(c.toUtc());
    final t = d.isNegative ? Duration.zero : d;
    String two(int n) => n.toString().padLeft(2, '0');
    return '${two(t.inHours)}:${two(t.inMinutes % 60)}:${two(t.inSeconds % 60)}';
  }

  Widget _chipBox(Widget child) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 9),
        height: 28,
        decoration: BoxDecoration(border: Border.all(color: C.line), borderRadius: BorderRadius.circular(8)),
        child: Center(child: child),
      );

  @override
  Widget build(BuildContext context) {
    final s = state;
    return Scaffold(
      backgroundColor: C.ink,
      body: SafeArea(
        child: joining
            ? const Loading()
            : error != null
                ? Stack(children: [
                    ErrorBox(message: error!, onRetry: _join),
                    Positioned(top: 14, right: 14, child: _exitBtn()),
                  ])
                : s == null
                    ? const Loading()
                    : Stack(children: [_content(s), if (effect != null) Positioned.fill(child: GiftEffect(gift: effect!, onDone: () => setState(() => effect = null)))]),
      ),
    );
  }

  Widget _exitBtn() => GestureDetector(
        onTap: () => Navigator.pop(context),
        child: Container(
          width: 40, height: 40,
          alignment: Alignment.center,
          decoration: BoxDecoration(color: C.surface, borderRadius: BorderRadius.circular(12), border: Border.all(color: C.line)),
          child: const Icn(Ic.power, size: 19, color: C.cream),
        ),
      );

  Widget _content(RoomState s) {
    final speaking = voice.speaking;
    final muted = voice.muted;
    final host = s.seatAt(0);
    final hostName = Profiles.peek(s.ownerUid)?.name ?? '';
    return Column(children: [
      Padding(
        padding: const EdgeInsets.fromLTRB(14, 14, 14, 0),
        child: Row(children: [
          _exitBtn(),
          const SizedBox(width: 10),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(s.title, maxLines: 1, overflow: TextOverflow.ellipsis, style: kufi(15)),
              const SizedBox(height: 1),
              Row(children: [
                Text('ID ${s.id.replaceAll('-', '').substring(0, 6).toUpperCase()}', style: plex(11, color: C.muted)),
                const SizedBox(width: 10),
                const Icn(Ic.user, size: 12, color: C.muted),
                const SizedBox(width: 4),
                Text('${s.seats.length}', style: plex(11, color: C.muted)),
              ]),
            ]),
          ),
          if (s.ownerUid != me)
            GestureDetector(
              onTap: _toggleFollow,
              child: Container(
                height: 34,
                padding: const EdgeInsets.symmetric(horizontal: 16),
                alignment: Alignment.center,
                decoration: BoxDecoration(color: following ? C.surface2 : C.amber, borderRadius: BorderRadius.circular(17)),
                child: Text(following ? 'تمت المتابعة' : 'متابعة', style: plex(12.5, w: FontWeight.w600, color: following ? C.muted : C.onAmber)),
              ),
            ),
        ]),
      ),
      Padding(
        padding: const EdgeInsets.fromLTRB(14, 12, 14, 0),
        child: Row(children: [
          _chipBox(Row(children: [const Icn(Ic.clock, size: 13, color: C.muted), const SizedBox(width: 5), Text(_elapsed(), style: plex(11.5, color: C.muted))])),
          const SizedBox(width: 8),
          _chipBox(Row(children: [Text(compactNumber(s.giftTotal), style: plex(11.5, color: C.muted)), const SizedBox(width: 5), const DiamondIcon(12, color: C.cream, opacity: .9)])),
        ]),
      ),
      const SizedBox(height: 24),
      Column(children: [
        Stack(clipBehavior: Clip.none, alignment: Alignment.topCenter, children: [
          Avatar(name: hostName, key2: s.ownerUid, size: 80, ring: speaking.contains(s.ownerUid), frame: s.ownerFrame),
          const Positioned(top: -10, child: Icn(Ic.crown, size: 18, color: C.amber, sw: 1.8)),
        ]),
        const SizedBox(height: 12),
        Text(hostName.isEmpty ? 'المضيف' : hostName, style: kufi(14)),
        const SizedBox(height: 5),
        _points(host?.points ?? 0, 10.5, 10, 17),
      ]),
      const SizedBox(height: 34),
      Padding(
        padding: const EdgeInsets.symmetric(horizontal: 7),
        child: GridView.count(
          crossAxisCount: 4,
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          mainAxisSpacing: 14,
          childAspectRatio: 82 / 108,
          children: [for (var i = 1; i < s.maxSeats; i++) _seat(s, i, speaking, muted)],
        ),
      ),
      Expanded(
        child: ListView.separated(
          controller: scroll,
          padding: const EdgeInsets.fromLTRB(14, 6, 14, 10),
          itemCount: messages.length,
          separatorBuilder: (_, __) => const SizedBox(height: 10),
          itemBuilder: (_, i) => _msg(messages[i]),
        ),
      ),
      _bottomBar(s),
    ]);
  }

  Widget _points(int v, double fs, double coin, double lh) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 8),
        height: lh,
        decoration: BoxDecoration(color: C.surface2, borderRadius: BorderRadius.circular(6)),
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          CoinIcon(coin),
          const SizedBox(width: 4),
          Text(groupedNumber(v), style: plex(fs, color: C.muted)),
        ]),
      );

  Widget _seat(RoomState s, int i, Set<String> speaking, Set<String> muted) {
    final seat = s.seatAt(i);
    return GestureDetector(
      onTap: () => _seatTap(i),
      child: Column(children: [
        SizedBox(
          width: 56, height: 56,
          child: seat == null
              ? Container(
                  decoration: BoxDecoration(shape: BoxShape.circle, border: Border.all(color: C.cream.withOpacity(.28), width: 1.5)),
                  alignment: Alignment.center,
                  child: const Icn(Ic.mic, size: 21.28, color: C.muted, sw: 1.5),
                )
              : Stack(clipBehavior: Clip.none, children: [
                  Avatar(name: Profiles.peek(seat.uid)?.name ?? '', key2: seat.uid, size: 56, ring: speaking.contains(seat.uid), frame: seat.frame),
                  if (muted.contains(seat.uid) || (seat.uid == me && !voice.micOn))
                    Positioned(
                      bottom: -1, right: -1,
                      child: Container(
                        width: 18, height: 18,
                        alignment: Alignment.center,
                        decoration: BoxDecoration(color: C.red, shape: BoxShape.circle, border: Border.all(color: C.ink, width: 2)),
                        child: const Icn(Ic.micOff, size: 10, color: Colors.white, sw: 2.2),
                      ),
                    ),
                ]),
        ),
        const SizedBox(height: 7),
        SizedBox(
          width: 80,
          child: Text(seat == null ? '${i + 1}' : (Profiles.peek(seat.uid)?.name ?? ''),
              textAlign: TextAlign.center, maxLines: 1, overflow: TextOverflow.ellipsis,
              style: plex(11.5, w: FontWeight.w500, color: seat == null ? C.muted : C.cream)),
        ),
        if (seat != null) ...[const SizedBox(height: 4), _points(seat.points, 10, 9, 16)],
      ]),
    );
  }

  Widget _msg(ChatMessage m) {
    final name = Profiles.peek(m.uid)?.name ?? '';
    if (m.kind == 'gift') {
      final nm = m.meta?['nameAr'] ?? '';
      return Center(
        child: Row(mainAxisSize: MainAxisSize.min, children: [
          const Icn(Ic.gift, size: 14, color: C.amber),
          const SizedBox(width: 7),
          Text('$name أرسل $nm', style: plex(11.5, color: C.muted)),
        ]),
      );
    }
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Text(name, style: plex(11, color: C.muted)),
      const SizedBox(height: 3),
      Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
        decoration: BoxDecoration(
          color: C.surface,
          border: Border.all(color: C.line),
          borderRadius: const BorderRadiusDirectional.only(
            topStart: Radius.circular(4), topEnd: Radius.circular(14), bottomStart: Radius.circular(14), bottomEnd: Radius.circular(14)),
        ),
        child: Text(m.body, style: plex(13)),
      ),
    ]);
  }

  Widget _bottomBar(RoomState s) => Container(
        height: 76,
        padding: const EdgeInsets.symmetric(horizontal: 10),
        decoration: const BoxDecoration(color: C.surface, border: Border(top: BorderSide(color: C.line))),
        child: Row(children: [
          GestureDetector(
            onTap: _openMenu,
            child: const SizedBox(width: 44, height: 44, child: Center(child: Icn(Ic.grid, size: 24, color: C.cream))),
          ),
          Expanded(
            child: Container(
              height: 42,
              margin: const EdgeInsets.symmetric(horizontal: 8),
              padding: const EdgeInsets.symmetric(horizontal: 16),
              decoration: BoxDecoration(color: C.surface2, borderRadius: BorderRadius.circular(21)),
              child: TextField(
                controller: input,
                onSubmitted: (_) => _sendText(),
                maxLength: 300,
                style: plex(13),
                cursorColor: C.amber,
                decoration: InputDecoration(border: InputBorder.none, counterText: '', hintText: 'اكتب رسالة…', hintStyle: plex(13, color: C.muted)),
              ),
            ),
          ),
          GestureDetector(
            onTap: _openGifts,
            child: Container(
              width: 48, height: 48,
              alignment: Alignment.center,
              decoration: const BoxDecoration(color: C.amber, shape: BoxShape.circle),
              child: const Icn(Ic.gift, size: 24, color: C.onAmber, sw: 1.8),
            ),
          ),
        ]),
      );
}

class _MenuSheet extends StatefulWidget {
  final String roomId;
  final RoomState state;
  final VoidCallback onChanged, onClosed;
  const _MenuSheet({required this.roomId, required this.state, required this.onChanged, required this.onClosed});
  @override
  State<_MenuSheet> createState() => _MenuSheetState();
}

class _MenuSheetState extends State<_MenuSheet> {
  final me = FirebaseAuth.instance.currentUser!.uid;
  List<Map>? requests;

  bool get isHost => widget.state.ownerUid == me;

  @override
  void initState() {
    super.initState();
    if (isHost) _loadReq();
  }

  Future<void> _loadReq() async {
    try {
      final r = await Api.i.get('/rooms/${widget.roomId}/seat-requests') as Map;
      final list = (r['requests'] as List).cast<Map>();
      for (final q in list) {
        Profiles.get(q['uid'] as String).then((_) => mounted ? setState(() {}) : null);
      }
      if (mounted) setState(() => requests = list);
    } catch (_) {}
  }

  Future<void> _decide(Map q, bool ok) async {
    try {
      await Api.i.post('/rooms/${widget.roomId}/seat-requests/${q['id']}/${ok ? 'approve' : 'reject'}');
      widget.onChanged();
      _loadReq();
    } on ApiException catch (e) {
      if (mounted) toast(context, e.message);
    }
  }

  Widget _row(String label, VoidCallback onTap, {Color color = C.cream}) => GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: Padding(padding: const EdgeInsets.symmetric(vertical: 14), child: Align(alignment: AlignmentDirectional.centerStart, child: Text(label, style: plex(14.5, w: FontWeight.w500, color: color)))),
      );

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 12, 20, 12),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Container(width: 36, height: 4, margin: const EdgeInsets.only(bottom: 8), decoration: BoxDecoration(color: C.cream.withOpacity(.18), borderRadius: BorderRadius.circular(2))),
          if (isHost && (requests?.isNotEmpty ?? false)) ...[
            Align(alignment: AlignmentDirectional.centerStart, child: Text('طلبات الصعود', style: kufi(14))),
            for (final q in requests!)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 6),
                child: Row(children: [
                  Expanded(child: Text(Profiles.peek(q['uid'] as String)?.name ?? '', style: plex(13.5))),
                  GestureDetector(onTap: () => _decide(q, false), child: Text('رفض', style: plex(13, color: C.muted))),
                  const SizedBox(width: 18),
                  GestureDetector(onTap: () => _decide(q, true), child: Text('قبول', style: plex(13, w: FontWeight.w600, color: C.amber))),
                ]),
              ),
            const Divider(color: C.line),
          ],
          if (widget.state.mySeat != null && !isHost)
            _row('النزول من المقعد', () async {
              try {
                await Api.i.post('/rooms/${widget.roomId}/seat/leave');
                widget.onChanged();
                if (mounted) Navigator.pop(context);
              } on ApiException catch (e) {
                if (mounted) toast(context, e.message);
              }
            }),
          if (isHost)
            _row('إغلاق الغرفة', () async {
              try {
                await Api.i.post('/rooms/${widget.roomId}/close');
                widget.onClosed();
              } on ApiException catch (e) {
                if (mounted) toast(context, e.message);
              }
            }, color: C.red),
          _row('مغادرة الغرفة', widget.onClosed),
        ]),
      ),
    );
  }
}
