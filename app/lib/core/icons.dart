import 'package:flutter/widgets.dart';
import 'package:flutter_svg/flutter_svg.dart';

/// أيقونات التصميم (نفس المسارات المستخدمة في نموذج Majlis).
class Ic {
  static const search = '<circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/>';
  static const crown = '<path d="M3 8l4.5 4L12 5l4.5 7L21 8l-2 11H5z"/>';
  static const bars = '<path d="M6 18V10M12 18V5M18 18v-6"/>';
  static const home = '<path d="M4 11 12 4l8 7v9h-5v-6H9v6H4z"/>';
  static const game =
      '<rect x="3" y="7" width="18" height="11" rx="5"/><path d="M8 10v5M5.5 12.5h5"/><circle cx="16" cy="11.5" r=".8"/><circle cx="18" cy="13.5" r=".8"/>';
  static const chat =
      '<path d="M4 6a3 3 0 0 1 3-3h10a3 3 0 0 1 3 3v8a3 3 0 0 1-3 3h-6l-5 4v-4a3 3 0 0 1-2-3z"/>';
  static const user = '<circle cx="12" cy="8" r="4"/><path d="M4 20c1-4 4-6 8-6s7 2 8 6"/>';
  static const power = '<path d="M12 3v8"/><path d="M6.5 6.5a8 8 0 1 0 11 0"/>';
  static const clock = '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>';
  static const mic = '<rect x="9" y="3" width="6" height="11" rx="3"/><path d="M5 11a7 7 0 0 0 14 0M12 18v3"/>';
  static const micOff = '<rect x="9" y="3" width="6" height="11" rx="3"/><path d="M5 11a7 7 0 0 0 12 5M3 3l18 18"/>';
  static const gift =
      '<rect x="3" y="9" width="18" height="12" rx="2"/><path d="M12 9v12M3 13h18M12 9c-3 0-5-1-5-3s3-3 5 3c2-6 5-5 5-3s-2 3-5 3"/>';
  static const grid =
      '<rect x="4" y="4" width="6.5" height="6.5" rx="2"/><rect x="13.5" y="4" width="6.5" height="6.5" rx="2"/><rect x="4" y="13.5" width="6.5" height="6.5" rx="2"/><rect x="13.5" y="13.5" width="6.5" height="6.5" rx="2"/>';
  static const settings =
      '<circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3M5 5l2 2M17 17l2 2M19 5l-2 2M7 17l-2 2"/>';
  static const headset =
      '<path d="M4 15v-3a8 8 0 0 1 16 0v3"/><rect x="3" y="14" width="4" height="6" rx="2"/><rect x="17" y="14" width="4" height="6" rx="2"/>';
  static const edit = '<path d="M4 20h4L19 9l-4-4L4 16z"/>';
  static const star = '<path d="m12 3 2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1L3.2 9.5l6.1-.9z"/>';
  static const shirt = '<path d="M8 3 3 6l2 4 3-1v11h8V9l3 1 2-4-5-3a4 4 0 0 1-8 0z"/>';
  static const bag = '<path d="M5 8h14l-1 12H6zM9 8a3 3 0 0 1 6 0"/>';
  static const mail = '<rect x="3" y="5" width="18" height="14" rx="3"/><path d="m4 7 8 6 8-6"/>';
  static const list = '<path d="M8 6h12M8 12h12M8 18h12M4 6h.01M4 12h.01M4 18h.01"/>';
  static const trophy =
      '<path d="M8 4h8v6a4 4 0 0 1-8 0zM8 6H4c0 3 1 4 4 4M16 6h4c0 3-1 4-4 4M12 14v4M8 20h8"/>';
  static const chevron = '<path d="m9 6 6 6-6 6"/>';
  // هدايا المتجر (بنفس أسلوب الخط)
  static const rose = '<circle cx="12" cy="9" r="4"/><path d="M12 13v8M12 17c-3 0-4-1-5-3M12 18c3 0 4-1 5-3"/>';
  static const heart = '<path d="M12 20s-7-4.5-7-10a4 4 0 0 1 7-2.5A4 4 0 0 1 19 10c0 5.5-7 10-7 10z"/>';
  static const coffee = '<path d="M5 8h11v6a5 5 0 0 1-5 5H10a5 5 0 0 1-5-5zM16 10h2a2 2 0 0 1 0 4h-2M8 3v2M12 3v2"/>';
  static const rocket =
      '<path d="M12 3c3 2 5 5 5 9l-2 4H9l-2-4c0-4 2-7 5-9zM9 16l-3 4M15 16l3 4"/><circle cx="12" cy="10" r="1.6"/>';
  static const car =
      '<path d="M4 15v-3l2-5h12l2 5v3zM4 15v3h3v-3M17 15v3h3v-3"/><circle cx="8" cy="12" r=".8"/><circle cx="16" cy="12" r=".8"/>';
  static const palace =
      '<path d="M4 20V10l3-2 2 2 3-4 3 4 2-2 3 2v10zM10 20v-5h4v5"/>';

  static String forGift(String id) {
    switch (id) {
      case 'rose': return rose;
      case 'heart': return heart;
      case 'coffee': return coffee;
      case 'star': return star;
      case 'crown': return crown;
      case 'rocket': return rocket;
      case 'car': return car;
      case 'palace': return palace;
      default: return gift;
    }
  }
}

/// يرسم أيقونة خطية بحجم ولون وسمك محددين.
class Icn extends StatelessWidget {
  final String paths;
  final double size;
  final Color color;
  final double sw;
  const Icn(this.paths, {super.key, this.size = 23, required this.color, this.sw = 1.6});

  @override
  Widget build(BuildContext context) {
    final hex = '#${color.value.toRadixString(16).padLeft(8, '0').substring(2)}';
    final svg =
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="$hex" stroke-width="$sw" stroke-linecap="round" stroke-linejoin="round">$paths</svg>';
    return SvgPicture.string(svg, width: size, height: size);
  }
}

/// عملة الذهب (دائرة كهرمانية بحلقة داخلية).
class CoinIcon extends StatelessWidget {
  final double size;
  const CoinIcon(this.size, {super.key});
  @override
  Widget build(BuildContext context) => SvgPicture.string(
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10" fill="#F4A93B"/><circle cx="12" cy="12" r="6.5" fill="none" stroke="#2A1A00" stroke-opacity=".45" stroke-width="1.6"/></svg>',
        width: size,
        height: size,
      );
}

class DiamondIcon extends StatelessWidget {
  final double size;
  final Color color;
  final double opacity;
  const DiamondIcon(this.size, {super.key, required this.color, this.opacity = 1});
  @override
  Widget build(BuildContext context) {
    final hex = '#${color.value.toRadixString(16).padLeft(8, '0').substring(2)}';
    return SvgPicture.string(
      '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M6 4h12l4 6-10 11L2 10z" fill="$hex" fill-opacity="$opacity"/></svg>',
      width: size,
      height: size,
    );
  }
}
