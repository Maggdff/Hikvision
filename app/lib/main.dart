import 'package:firebase_auth/firebase_auth.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import 'core/colors.dart';
import 'core/config.dart';
import 'ui/auth_screen.dart';
import 'ui/shell.dart';
import 'ui/widgets.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  String? initError;
  if (!Config.firebaseConfigured) {
    initError = 'إعدادات Firebase غير موجودة في هذا الإصدار';
  } else {
    try {
      await Firebase.initializeApp(
        options: const FirebaseOptions(
          apiKey: Config.fbApiKey,
          appId: Config.fbAppId,
          messagingSenderId: Config.fbSenderId,
          projectId: Config.fbProjectId,
          storageBucket: Config.fbStorageBucket,
        ),
      );
    } catch (e) {
      initError = 'تعذّر تهيئة Firebase';
    }
  }
  runApp(VoiceUpApp(initError: initError));
}

class VoiceUpApp extends StatelessWidget {
  final String? initError;
  const VoiceUpApp({super.key, this.initError});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'VoiceUp',
      debugShowCheckedModeBanner: false,
      locale: const Locale('ar'),
      supportedLocales: const [Locale('ar'), Locale('en')],
      localizationsDelegates: const [
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      theme: ThemeData(
        brightness: Brightness.dark,
        scaffoldBackgroundColor: C.ink,
        colorScheme: const ColorScheme.dark(primary: C.amber, onPrimary: C.onAmber, surface: C.surface),
        useMaterial3: true,
      ),
      home: initError != null
          ? Scaffold(body: Center(child: Padding(padding: const EdgeInsets.all(32), child: Text(initError!, style: plex(14, color: C.muted)))))
          : StreamBuilder<User?>(
              stream: FirebaseAuth.instance.authStateChanges(),
              builder: (context, snap) {
                if (snap.connectionState == ConnectionState.waiting) {
                  return const Scaffold(body: Loading());
                }
                return snap.data == null ? const AuthScreen() : const Shell();
              },
            ),
    );
  }
}
