import 'package:flutter/widgets.dart';
import 'package:svgaplayer_flutter/svgaplayer_flutter.dart';

/// ملفات SVGA المضمّنة. المفتاح هو المعرّف القادم من الخادم (frames.id / gifts.id).
class SvgaAssets {
  static const frames = <String, String>{'vip6': 'assets/svga/vip6_frame.svga'};
  static const gifts = <String, String>{'bugatti': 'assets/svga/bugatti_gift.svga'};

  static final Map<String, Future<MovieEntity>> _cache = {};

  /// فك الترميز مرة واحدة لكل ملف ومشاركته بين كل العناصر (يوفّر الذاكرة).
  static Future<MovieEntity> load(String asset) =>
      _cache.putIfAbsent(asset, () => SVGAParser.shared.decodeFromAssets(asset));

  /// تأثيرات الهدايا كبيرة، فلا نبقيها في الذاكرة بعد انتهائها.
  static void evict(String asset) => _cache.remove(asset);
}

/// تشغيل SVGA: متكرر (إطارات) أو مرة واحدة (هدايا). يحافظ على الحركة والشفافية الأصلية.
class SvgaView extends StatefulWidget {
  final String asset;
  final bool loop;
  final BoxFit fit;
  final VoidCallback? onFinished;
  const SvgaView({super.key, required this.asset, this.loop = true, this.fit = BoxFit.contain, this.onFinished});

  @override
  State<SvgaView> createState() => _SvgaViewState();
}

class _SvgaViewState extends State<SvgaView> with SingleTickerProviderStateMixin {
  late final SVGAAnimationController ctl = SVGAAnimationController(vsync: this);
  bool ready = false;

  @override
  void initState() {
    super.initState();
    _start();
  }

  Future<void> _start() async {
    try {
      final video = await SvgaAssets.load(widget.asset);
      if (!mounted) return;
      ctl.videoItem = video;
      if (widget.loop) {
        ctl.repeat();
      } else {
        ctl.addStatusListener((s) {
          if (s == AnimationStatus.completed) widget.onFinished?.call();
        });
        ctl.forward(from: 0);
      }
      setState(() => ready = true);
    } catch (_) {
      // ملف تالف أو نقص ذاكرة: لا نُسقط الشاشة، ونُنهي التأثير فقط
      widget.onFinished?.call();
    }
  }

  @override
  void dispose() {
    ctl.dispose();
    if (!widget.loop) SvgaAssets.evict(widget.asset);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (!ready) return const SizedBox.shrink();
    return SVGAImage(ctl, fit: widget.fit, clearsAfterStop: true);
  }
}
