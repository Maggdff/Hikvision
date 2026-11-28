import 'package:flutter/material.dart';

import '../core/api.dart';
import '../core/catalog.dart';
import '../core/colors.dart';
import '../core/icons.dart';
import 'widgets.dart';

/// إدارة الهدايا والإطارات (للمشرفين فقط): الأسماء والأسعار والفئة والترتيب والتفعيل ومسارات الأصول على Cloudflare.
/// رفع الملفات نفسها يتم من لوحة الاستضافة R2؛ هنا نكتب مسار الملف داخل الـ bucket.
class AdminCatalogScreen extends StatefulWidget {
  const AdminCatalogScreen({super.key});
  @override
  State<AdminCatalogScreen> createState() => _AdminCatalogScreenState();
}

class _AdminCatalogScreenState extends State<AdminCatalogScreen> {
  List<Map>? gifts, frames;
  String base = '';
  String? error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final r = await Api.i.get('/admin/catalog') as Map;
      if (!mounted) return;
      setState(() {
        gifts = (r['gifts'] as List).cast<Map>();
        frames = (r['frames'] as List).cast<Map>();
        base = (r['assetsBaseUrl'] ?? '') as String;
        error = null;
      });
    } on ApiException catch (e) {
      if (mounted) setState(() => error = e.code == 'admin_only' ? 'هذه الشاشة للمشرفين فقط' : e.message);
    }
  }

  Future<void> _edit({required bool isGift, Map? item}) async {
    final isNew = item == null;
    final id = TextEditingController(text: item?['id'] as String? ?? '');
    final ar = TextEditingController(text: item?['nameAr'] as String? ?? '');
    final en = TextEditingController(text: item?['nameEn'] as String? ?? '');
    final price = TextEditingController(text: '${item?['priceCoins'] ?? ''}');
    final sort = TextEditingController(text: '${item?['sortOrder'] ?? 0}');
    final icon = TextEditingController(text: item?['iconPath'] as String? ?? '');
    final effect = TextEditingController(text: (isGift ? item?['effectPath'] : item?['assetPath']) as String? ?? '');
    var tier = (item?['tier'] as String?) ?? 'small';
    var active = (item?['active'] as bool?) ?? true;

    InputDecoration dec(String h) => InputDecoration(labelText: h, labelStyle: plex(12, color: C.muted), isDense: true);
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => Directionality(
        textDirection: TextDirection.rtl,
        child: StatefulBuilder(
          builder: (ctx, setD) => AlertDialog(
            backgroundColor: C.surface,
            title: Text(isNew ? (isGift ? 'هدية جديدة' : 'إطار جديد') : 'تعديل', style: kufi(16)),
            content: SingleChildScrollView(
              child: Column(mainAxisSize: MainAxisSize.min, children: [
                if (isNew) TextField(controller: id, style: plex(13), decoration: dec('المعرّف (a-z و 0-9 و _)')),
                TextField(controller: ar, style: plex(13), decoration: dec('الاسم بالعربية')),
                TextField(controller: en, style: plex(13), decoration: dec('الاسم بالإنجليزية')),
                TextField(controller: price, keyboardType: TextInputType.number, style: plex(13), decoration: dec('السعر (عملات)')),
                TextField(controller: sort, keyboardType: const TextInputType.numberWithOptions(signed: true), style: plex(13), decoration: dec('الترتيب')),
                if (isGift) ...[
                  const SizedBox(height: 8),
                  DropdownButtonFormField<String>(
                    value: tier,
                    dropdownColor: C.surface2,
                    decoration: dec('الفئة (تحدد شدة التأثير الافتراضي)'),
                    items: const [
                      DropdownMenuItem(value: 'small', child: Text('small')),
                      DropdownMenuItem(value: 'medium', child: Text('medium')),
                      DropdownMenuItem(value: 'large', child: Text('large')),
                    ],
                    onChanged: (v) => setD(() => tier = v ?? tier),
                  ),
                  TextField(controller: icon, style: plex(13), decoration: dec('مسار الأيقونة (مثل gifts/crown.webp)')),
                ],
                TextField(controller: effect, style: plex(13), decoration: dec(isGift ? 'مسار تأثير SVGA (مثل gifts/crown.svga)' : 'مسار إطار SVGA (مثل frames/vip7.svga)')),
                SwitchListTile(
                  value: active,
                  onChanged: (v) => setD(() => active = v),
                  activeColor: C.amber,
                  contentPadding: EdgeInsets.zero,
                  title: Text('مفعّل (ظاهر للمستخدمين)', style: plex(13)),
                ),
              ]),
            ),
            actions: [
              TextButton(onPressed: () => Navigator.pop(ctx, false), child: Text('إلغاء', style: plex(14, color: C.muted))),
              TextButton(onPressed: () => Navigator.pop(ctx, true), child: Text('حفظ', style: plex(14, w: FontWeight.w600, color: C.amber))),
            ],
          ),
        ),
      ),
    );
    if (ok != true) return;
    final itemId = isNew ? id.text.trim() : item['id'] as String;
    final p = int.tryParse(price.text.trim());
    final so = int.tryParse(sort.text.trim()) ?? 0;
    if (p == null) {
      toast(context, 'السعر غير صالح');
      return;
    }
    String? nz(TextEditingController c) => c.text.trim().isEmpty ? null : c.text.trim();
    final body = <String, Object?>{
      'nameAr': ar.text.trim(), 'nameEn': en.text.trim(), 'priceCoins': p, 'sortOrder': so, 'active': active,
      if (isGift) ...{'tier': tier, 'iconPath': nz(icon), 'effectPath': nz(effect)} else 'assetPath': nz(effect),
    };
    try {
      await Api.i.put('/admin/${isGift ? 'gifts' : 'frames'}/$itemId', body: body);
      if (!mounted) return;
      toast(context, 'تم الحفظ');
      await _load();
      Catalog.load();
    } on ApiException catch (e) {
      if (mounted) toast(context, _msg(e));
    }
  }

  String _msg(ApiException e) {
    switch (e.code) {
      case 'invalid_path': return 'المسار غير صالح: حروف إنجليزية وأرقام و / و . و _ و - فقط، أو رابط https كامل';
      case 'invalid_id': return 'المعرّف غير صالح';
      case 'missing_fields': return 'الاسمان والسعر مطلوبة للعنصر الجديد';
      case 'invalid_price': return 'السعر يجب أن يكون عدداً صحيحاً موجباً';
      default: return e.message;
    }
  }

  Widget _tile(Map it, bool isGift) {
    final active = it['active'] as bool;
    final path = isGift ? (it['effectPath'] ?? it['iconPath']) : it['assetPath'];
    return GestureDetector(
      onTap: () => _edit(isGift: isGift, item: it),
      child: Opacity(
        opacity: active ? 1 : .5,
        child: Container(
          margin: const EdgeInsets.only(bottom: 8),
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(color: C.surface, borderRadius: BorderRadius.circular(14), border: Border.all(color: C.line)),
          child: Row(children: [
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text('${it['nameAr']}  ·  ${it['id']}', style: plex(13.5, w: FontWeight.w500)),
                const SizedBox(height: 3),
                Text(path == null ? 'بدون ملف أصل (رسم افتراضي)' : '$path', style: plex(11, color: C.muted)),
              ]),
            ),
            const CoinIcon(14),
            const SizedBox(width: 4),
            Text(groupedNumber(it['priceCoins'] as int), style: plex(12.5, color: C.amber)),
          ]),
        ),
      ),
    );
  }

  String groupedNumber(int n) {
    final s = n.toString();
    final b = StringBuffer();
    for (var i = 0; i < s.length; i++) {
      if (i > 0 && (s.length - i) % 3 == 0) b.write(',');
      b.write(s[i]);
    }
    return b.toString();
  }

  Widget _section(String title, bool isGift, List<Map> items) => Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Row(children: [
          Expanded(child: Text(title, style: kufi(15))),
          GestureDetector(onTap: () => _edit(isGift: isGift), child: const Icn('<path d="M12 5v14M5 12h14"/>', size: 22, color: C.amber)),
        ]),
        const SizedBox(height: 10),
        for (final it in items) _tile(it, isGift),
        const SizedBox(height: 16),
      ]);

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
              Expanded(child: Text('إدارة الهدايا والإطارات', style: kufi(17))),
            ]),
          ),
          Expanded(
            child: error != null
                ? ErrorBox(message: error!, onRetry: _load)
                : gifts == null
                    ? const Loading()
                    : ListView(padding: const EdgeInsets.fromLTRB(16, 8, 16, 24), children: [
                        if (base.isEmpty)
                          Container(
                            margin: const EdgeInsets.only(bottom: 14),
                            padding: const EdgeInsets.all(12),
                            decoration: BoxDecoration(color: C.surface2, borderRadius: BorderRadius.circular(12)),
                            child: Text('ASSETS_BASE_URL غير مضبوط على الخادم: المسارات النسبية لن تظهر. استخدم رابط https كاملاً أو اضبط المتغير على Railway.', style: plex(12, color: C.amber)),
                          )
                        else
                          Padding(padding: const EdgeInsets.only(bottom: 14), child: Text('أساس الروابط: $base', style: plex(11.5, color: C.muted))),
                        _section('الهدايا', true, gifts!),
                        _section('الإطارات', false, frames!),
                      ]),
          ),
        ]),
      ),
    );
  }
}
