import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

import '../core/colors.dart';
import 'svga.dart';

TextStyle kufi(double size, {FontWeight w = FontWeight.w600, Color color = C.cream, double? height}) =>
    GoogleFonts.notoKufiArabic(fontSize: size, fontWeight: w, color: color, height: height);

TextStyle plex(double size, {FontWeight w = FontWeight.w400, Color color = C.cream, double? height}) =>
    GoogleFonts.ibmPlexSansArabic(fontSize: size, fontWeight: w, color: color, height: height);

/// أفاتار دائري بحرف الاسم، بحلقة كهرمانية عند التحدث.
class Avatar extends StatelessWidget {
  final String name;
  final String key2;
  final double size;
  final bool ring;
  /// معرّف الإطار المتحرك (SVGA) إن وُجد، مثل 'vip6'.
  final String? frame;
  const Avatar({super.key, required this.name, required this.key2, required this.size, this.ring = false, this.frame});

  @override
  Widget build(BuildContext context) {
    final p = C.paletteFor(key2);
    final letter = name.trim().isEmpty ? '؟' : String.fromCharCode(name.trim().runes.first);
    final frameAsset = frame == null ? null : SvgaAssets.frames[frame];
    return SizedBox(
      width: size,
      height: size,
      child: Stack(clipBehavior: Clip.none, children: [
        if (ring && frameAsset == null)
          Positioned(
            left: -4, right: -4, top: -4, bottom: -4,
            child: DecoratedBox(
              decoration: BoxDecoration(shape: BoxShape.circle, border: Border.all(color: C.amber, width: 2)),
            ),
          ),
        Container(
          width: size,
          height: size,
          alignment: Alignment.center,
          decoration: BoxDecoration(shape: BoxShape.circle, color: p[0]),
          child: Text(letter, style: kufi(size * 0.38, color: p[1])),
        ),
        // الإطار أكبر من الصورة بـ 1.7 مرة؛ فتحته الداخلية تطابق الصورة. يُرسم فوقها دون قص.
        if (frameAsset != null)
          Positioned(
            left: -size * 0.35, top: -size * 0.35, width: size * 1.7, height: size * 1.7,
            child: IgnorePointer(child: SvgaView(asset: frameAsset)),
          ),
      ]),
    );
  }
}

void toast(BuildContext context, String msg) {
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(
      content: Text(msg, style: plex(13)),
      backgroundColor: C.surface2,
      behavior: SnackBarBehavior.floating,
    ));
}

class Loading extends StatelessWidget {
  const Loading({super.key});
  @override
  Widget build(BuildContext context) =>
      const Center(child: CircularProgressIndicator(color: C.amber, strokeWidth: 2.5));
}

class ErrorBox extends StatelessWidget {
  final String message;
  final VoidCallback onRetry;
  const ErrorBox({super.key, required this.message, required this.onRetry});
  @override
  Widget build(BuildContext context) => Center(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 32),
            child: Text(message, textAlign: TextAlign.center, style: plex(13, color: C.muted)),
          ),
          const SizedBox(height: 14),
          GestureDetector(
            onTap: onRetry,
            child: Container(
              height: 34,
              padding: const EdgeInsets.symmetric(horizontal: 18),
              alignment: Alignment.center,
              decoration: BoxDecoration(color: C.amber, borderRadius: BorderRadius.circular(17)),
              child: Text('إعادة المحاولة', style: plex(12.5, w: FontWeight.w600, color: C.onAmber)),
            ),
          ),
        ]),
      );
}
