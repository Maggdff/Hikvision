import 'package:flutter/material.dart';

import '../core/api.dart';
import '../core/colors.dart';
import '../core/icons.dart';
import '../core/models.dart';
import 'svga.dart';
import 'widgets.dart';

/// متجر الإطارات: معاينة متحركة (SVGA) + شراء من الرصيد + تفعيل/إزالة.
class StoreScreen extends StatefulWidget {
  const StoreScreen({super.key});
  @override
  State<StoreScreen> createState() => _StoreScreenState();
}

class _StoreScreenState extends State<StoreScreen> {
  List<FrameItem>? frames;
  int? balance;
  String? error;
  String? busyId;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final f = await Api.i.get('/store/frames') as Map;
      final w = await Api.i.get('/wallet') as Map;
      if (!mounted) return;
      setState(() {
        frames = (f['frames'] as List).map((e) => FrameItem.fromJson(e as Map)).toList();
        balance = (w['balance'] as num).toInt();
        error = null;
      });
    } on ApiException catch (e) {
      if (mounted) setState(() => error = e.message);
    }
  }

  Future<void> _act(FrameItem f) async {
    if (busyId != null) return;
    setState(() => busyId = f.id);
    try {
      if (!f.owned) {
        await Api.i.post('/store/frames/${f.id}/buy', idemKey: Api.newKey());
      } else {
        await Api.i.put('/me/frame', body: {'frameId': f.equipped ? null : f.id});
      }
      await _load();
    } on ApiException catch (e) {
      if (mounted) toast(context, e.message);
    }
    if (mounted) setState(() => busyId = null);
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: C.ink,
      body: SafeArea(
        child: Column(children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(14, 14, 14, 8),
            child: Row(children: [
              GestureDetector(
                onTap: () => Navigator.pop(context),
                child: Container(
                  width: 40, height: 40,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(color: C.surface, borderRadius: BorderRadius.circular(12), border: Border.all(color: C.line)),
                  child: Transform.scale(scaleX: -1, child: const Icn(Ic.chevron, size: 19, color: C.cream)),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(child: Text('متجر الإطارات', style: kufi(17))),
              Text(balance == null ? '' : groupedNumber(balance!), style: plex(14, w: FontWeight.w600)),
              const SizedBox(width: 6),
              const CoinIcon(18),
            ]),
          ),
          Expanded(child: _body()),
        ]),
      ),
    );
  }

  Widget _body() {
    if (error != null) return ErrorBox(message: error!, onRetry: _load);
    if (frames == null) return const Loading();
    return GridView.builder(
      padding: const EdgeInsets.all(16),
      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount: 2, crossAxisSpacing: 12, mainAxisSpacing: 12, mainAxisExtent: 250),
      itemCount: frames!.length,
      itemBuilder: (_, i) {
        final f = frames![i];
        final asset = f.assetUrl;
        final busy = busyId == f.id;
        final label = !f.owned ? 'شراء' : (f.equipped ? 'إزالة' : 'تفعيل');
        return Container(
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(color: C.surface, borderRadius: BorderRadius.circular(18), border: Border.all(color: C.line)),
          child: Column(children: [
            Expanded(child: asset == null ? const SizedBox() : SvgaView(url: asset)),
            const SizedBox(height: 8),
            Text(f.nameAr, style: plex(13, w: FontWeight.w500)),
            const SizedBox(height: 6),
            Row(mainAxisAlignment: MainAxisAlignment.center, children: [
              const CoinIcon(12),
              const SizedBox(width: 4),
              Text(groupedNumber(f.priceCoins), style: plex(12, w: FontWeight.w500, color: C.amber)),
            ]),
            const SizedBox(height: 10),
            GestureDetector(
              onTap: () => _act(f),
              child: Container(
                height: 36,
                alignment: Alignment.center,
                decoration: BoxDecoration(color: f.equipped ? C.surface2 : C.amber, borderRadius: BorderRadius.circular(18)),
                child: busy
                    ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2, color: C.onAmber))
                    : Text(label, style: plex(13, w: FontWeight.w600, color: f.equipped ? C.cream : C.onAmber)),
              ),
            ),
          ]),
        );
      },
    );
  }
}
