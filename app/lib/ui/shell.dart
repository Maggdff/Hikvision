import 'package:flutter/material.dart';

import '../core/colors.dart';
import '../core/icons.dart';
import 'home_screen.dart';
import 'profile_screen.dart';
import 'widgets.dart';

/// الشريط السفلي. الألعاب والرسائل غير مبنية بعد، فلا تظهر أزرار لا تعمل.
class Shell extends StatefulWidget {
  const Shell({super.key});
  @override
  State<Shell> createState() => _ShellState();
}

class _ShellState extends State<Shell> {
  int tab = 0;

  Widget _item(int i, String icon, String label) {
    final on = tab == i;
    final col = on ? C.amber : C.muted;
    return Expanded(
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: () => setState(() => tab = i),
        child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
          Icn(icon, size: 23, color: col, sw: 1.7),
          const SizedBox(height: 4),
          Text(label, style: plex(10.5, color: col)),
        ]),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: tab == 1 ? C.paper : C.ink,
      body: IndexedStack(index: tab, children: const [HomeScreen(), ProfileScreen()]),
      bottomNavigationBar: Container(
        height: 70 + MediaQuery.of(context).padding.bottom,
        padding: EdgeInsets.fromLTRB(8, 0, 8, MediaQuery.of(context).padding.bottom),
        decoration: const BoxDecoration(color: C.surface, border: Border(top: BorderSide(color: C.line))),
        child: Row(children: [_item(0, Ic.home, 'الرئيسية'), _item(1, Ic.user, 'أنا')]),
      ),
    );
  }
}
