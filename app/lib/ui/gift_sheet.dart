import 'package:flutter/material.dart';

import '../core/api.dart';
import '../core/colors.dart';
import '../core/icons.dart';
import '../core/models.dart';
import '../core/profiles.dart';
import 'widgets.dart';

/// ورقة الهدايا: اختيار المستلم من أصحاب المقاعد ثم الهدية ثم إرسال (الخصم من الخادم/الدفتر).
class GiftSheet extends StatefulWidget {
  final String roomId;
  final String myUid;
  final List<Seat> seats;
  final void Function(Gift gift) onSent;
  const GiftSheet({super.key, required this.roomId, required this.myUid, required this.seats, required this.onSent});

  @override
  State<GiftSheet> createState() => _GiftSheetState();
}

class _GiftSheetState extends State<GiftSheet> {
  List<Gift>? gifts;
  int? balance;
  String? error;
  Gift? picked;
  String? recipient;
  bool sending = false;

  @override
  void initState() {
    super.initState();
    final others = widget.seats.where((s) => s.uid != widget.myUid).toList();
    if (others.isNotEmpty) recipient = others.first.uid;
    for (final s in widget.seats) {
      Profiles.get(s.uid).then((_) => mounted ? setState(() {}) : null);
    }
    _load();
  }

  Future<void> _load() async {
    try {
      final g = await Api.i.get('/gifts') as Map;
      final w = await Api.i.get('/wallet') as Map;
      if (!mounted) return;
      setState(() {
        gifts = (g['gifts'] as List).map((e) => Gift.fromJson(e as Map)).toList();
        balance = (w['balance'] as num).toInt();
      });
    } on ApiException catch (e) {
      if (mounted) setState(() => error = e.message);
    }
  }

  Future<void> _send() async {
    final g = picked, r = recipient;
    if (g == null || r == null || sending) return;
    setState(() => sending = true);
    try {
      final res = await Api.i.post('/rooms/${widget.roomId}/gifts',
          body: {'giftId': g.id, 'recipientUid': r}, idemKey: Api.newKey()) as Map;
      balance = (res['balance'] as num?)?.toInt() ?? balance;
      widget.onSent(g);
      if (mounted) Navigator.pop(context);
    } on ApiException catch (e) {
      if (mounted) {
        setState(() => sending = false);
        toast(context, e.message);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final others = widget.seats.where((s) => s.uid != widget.myUid).toList();
    return Container(
      height: 520,
      padding: const EdgeInsets.fromLTRB(14, 10, 14, 0),
      decoration: const BoxDecoration(
        color: C.surface,
        borderRadius: BorderRadius.vertical(top: Radius.circular(22)),
        boxShadow: [BoxShadow(color: Color(0x80000000), blurRadius: 40, offset: Offset(0, -12))],
      ),
      child: Column(children: [
        Container(width: 36, height: 4, margin: const EdgeInsets.only(bottom: 14), decoration: BoxDecoration(color: C.cream.withOpacity(.18), borderRadius: BorderRadius.circular(2))),
        if (others.isEmpty)
          Padding(
            padding: const EdgeInsets.only(bottom: 6),
            child: Text('لا يوجد أحد على المقاعد لاستلام الهدية', style: plex(12, color: C.muted)),
          )
        else
          Wrap(spacing: 10, alignment: WrapAlignment.center, children: [
            for (final s in others)
              GestureDetector(
                onTap: () => setState(() => recipient = s.uid),
                child: Padding(
                  padding: const EdgeInsets.all(4),
                  child: Avatar(name: Profiles.peek(s.uid)?.name ?? '', key2: s.uid, size: 40, ring: recipient == s.uid),
                ),
              ),
          ]),
        Container(
          margin: const EdgeInsets.fromLTRB(4, 16, 4, 8),
          decoration: const BoxDecoration(border: Border(bottom: BorderSide(color: C.line))),
          child: Row(children: [
            Container(
              padding: const EdgeInsets.only(bottom: 10),
              decoration: const BoxDecoration(border: Border(bottom: BorderSide(color: C.amber, width: 2))),
              child: Text('الهدايا', style: plex(14, w: FontWeight.w600)),
            ),
          ]),
        ),
        Expanded(child: _grid()),
        Padding(
          padding: EdgeInsets.fromLTRB(4, 8, 4, 12 + MediaQuery.of(context).padding.bottom),
          child: Row(children: [
            Container(
              height: 46,
              padding: const EdgeInsets.symmetric(horizontal: 16),
              child: Row(children: [
                Text(balance == null ? '—' : groupedNumber(balance!), style: plex(15, w: FontWeight.w600)),
                const SizedBox(width: 8),
                const CoinIcon(18),
              ]),
            ),
            const Spacer(),
            GestureDetector(
              onTap: (picked == null || recipient == null) ? null : _send,
              child: Opacity(
                opacity: (picked == null || recipient == null) ? .5 : 1,
                child: Container(
                  height: 46,
                  width: 120,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(color: C.amber, borderRadius: BorderRadius.circular(23)),
                  child: sending
                      ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2, color: C.onAmber))
                      : Text('إرسال', style: plex(15, w: FontWeight.w600, color: C.onAmber)),
                ),
              ),
            ),
          ]),
        ),
      ]),
    );
  }

  Widget _grid() {
    if (error != null) return ErrorBox(message: error!, onRetry: _load);
    if (gifts == null) return const Loading();
    return GridView.builder(
      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 4, crossAxisSpacing: 4, mainAxisSpacing: 4, mainAxisExtent: 124),
      itemCount: gifts!.length,
      itemBuilder: (_, i) {
        final g = gifts![i];
        final on = picked?.id == g.id;
        return GestureDetector(
          onTap: () => setState(() => picked = g),
          child: Container(
            padding: const EdgeInsets.fromLTRB(2, 12, 2, 10),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(14),
              border: Border.all(color: on ? C.amber : Colors.transparent, width: 1.5),
              color: on ? C.amber.withOpacity(.08) : Colors.transparent,
            ),
            child: Column(children: [
              Container(
                width: 56, height: 56,
                alignment: Alignment.center,
                decoration: BoxDecoration(color: C.surface2, borderRadius: BorderRadius.circular(16)),
                child: Icn(Ic.forGift(g.id), size: 30, color: C.amber, sw: 1.5),
              ),
              const SizedBox(height: 8),
              Text(g.nameAr, style: plex(12)),
              const SizedBox(height: 8),
              Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                const CoinIcon(12),
                const SizedBox(width: 3),
                Text(groupedNumber(g.priceCoins), style: plex(11.5, w: FontWeight.w500, color: C.amber)),
              ]),
            ]),
          ),
        );
      },
    );
  }
}

/// تأثير هدية خفيف: أيقونة تتكبّر وتتلاشى مع جسيمات. الشدة حسب فئة الهدية.
class GiftEffect extends StatefulWidget {
  final Gift gift;
  final VoidCallback onDone;
  const GiftEffect({super.key, required this.gift, required this.onDone});
  @override
  State<GiftEffect> createState() => _GiftEffectState();
}

class _GiftEffectState extends State<GiftEffect> with SingleTickerProviderStateMixin {
  late final AnimationController c;

  @override
  void initState() {
    super.initState();
    final ms = widget.gift.tier == 'large' ? 4400 : widget.gift.tier == 'medium' ? 3000 : 2200;
    c = AnimationController(vsync: this, duration: Duration(milliseconds: ms))
      ..addStatusListener((s) {
        if (s == AnimationStatus.completed) widget.onDone();
      })
      ..forward();
  }

  @override
  void dispose() {
    c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final big = widget.gift.tier == 'large' ? 150.0 : widget.gift.tier == 'medium' ? 110.0 : 76.0;
    return IgnorePointer(
      child: AnimatedBuilder(
        animation: c,
        builder: (_, __) {
          final t = c.value;
          final scale = Curves.easeOutBack.transform((t * 4).clamp(0.0, 1.0));
          final fade = t < .75 ? 1.0 : (1 - (t - .75) / .25);
          return Opacity(
            opacity: fade.clamp(0.0, 1.0),
            child: Stack(alignment: Alignment.center, children: [
              CustomPaint(size: Size.infinite, painter: _Sparks(t, widget.gift.tier == 'large' ? 28 : 14)),
              Transform.scale(scale: scale, child: Icn(Ic.forGift(widget.gift.id), size: big, color: C.amber, sw: 1.3)),
            ]),
          );
        },
      ),
    );
  }
}

class _Sparks extends CustomPainter {
  final double t;
  final int n;
  _Sparks(this.t, this.n);
  @override
  void paint(Canvas canvas, Size size) {
    final c = Offset(size.width / 2, size.height / 2);
    final p = Paint()..color = C.amber.withOpacity((1 - t).clamp(0.0, 1.0));
    for (var i = 0; i < n; i++) {
      final a = i * 6.2831853 / n;
      final r = 40 + t * (size.shortestSide * .45) * (0.6 + (i % 3) * .2);
      canvas.drawCircle(c + Offset.fromDirection(a, r), 3 + (i % 3).toDouble(), p);
    }
  }

  @override
  bool shouldRepaint(_Sparks o) => o.t != t;
}
