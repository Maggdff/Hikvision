# VoiceUp

تطبيق Android اجتماعي صوتي عربي (غرف صوتية)، مع Backend ولوحات ويب.

## الهيكل

| المجلد | المحتوى |
|---|---|
| `android/` | تطبيق Kotlin + Jetpack Compose + Material 3 (MVVM + Clean Architecture) |
| `backend/` | API (Node.js/TypeScript) + PostgreSQL + Migrations + اختبارات |
| `admin-web/` | لوحة الإدارة ولوحة الوكالات (Next.js، RTL) |
| `firebase/` | قواعد Firestore وStorage (تُنشر عبر Firebase CLI أو الكونسول) |
| `docs/PLAN.md` | الخطة والمعمارية وتقدير التكلفة |

## الحالة

المرحلة الحالية: **المرحلة 2**: Backend الغرف الصوتية مكتمل ومُختبَر (24 اختباراً). واجهة Android للغرف مكتوبة (قائمة، إنشاء، غرفة، مقاعد، إشراف، LiveKit) لكنها **لم تُترجَم ولم تُختبَر** لعدم توفر Android SDK هنا؛ أول بناء في GitHub Actions سيكشف الأخطاء. ملفات النشر في `docs/DEPLOY-BACKEND.md`.

## قواعد المشروع

- لا أسرار أو مفاتيح API في التطبيق أو المستودع (`.gitignore` يستثني `google-services.json` و`.env` ومفاتيح التوقيع). تُحفظ في GitHub Secrets / Secret Manager.
- المال: PostgreSQL فقط، `NUMERIC`، دفتر قيود غير قابل للتعديل، Idempotency Keys.
- لا قيم مالية ثابتة في الكود؛ كلها قواعد مُصدَرة وقابلة للإدارة.
- السحب المالي مغلق بـ Feature Flag حتى اجتياز المراجعة القانونية.
- كل مرحلة تُختبر قبل الانتقال إلى التالية.
