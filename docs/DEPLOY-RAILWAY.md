# نشر الخادم على Railway

## التكلفة (تحقق منها في railway.com/pricing قبل الاعتماد)
Railway يعطي رصيد تجربة محدوداً ثم يحاسب حسب الاستهلاك (خطة Hobby نحو 5$ شهرياً تشمل رصيد استخدام بنفس القيمة). الخادم + PostgreSQL الصغيران غالباً ضمن هذا الرصيد أثناء التجربة.

## الخطوات
1. railway.com ← New Project ← Deploy from GitHub repo ← اختر مستودعك.
2. في إعدادات الخدمة: **Root Directory = `backend`** (إن كان المستودع يحوي مجلد المشروع، اكتب المسار الكامل مثل `VoiceUp/backend`). الملف `railway.json` يختار Dockerfile ومسار الفحص `/health`.
3. أضف قاعدة بيانات: New ← Database ← PostgreSQL.
4. في خدمة الخادم ← Variables:
   - `DATABASE_URL` = `${{Postgres.DATABASE_URL}}` (مرجع لقاعدة Railway، اختر من القائمة)
   - `LIVEKIT_URL`، `LIVEKIT_API_KEY`، `LIVEKIT_API_SECRET` (مفاتيحك الجديدة بعد التدوير)
   - `FIREBASE_PROJECT_ID` = `voice-up-84ee1`
   - `RUN_MIGRATIONS` = `true`
   - `ADMIN_UIDS` = معرّفك في Firebase (اختياري، لمنح عملات)
5. Settings ← Networking ← Generate Domain. ستحصل على `https://xxxx.up.railway.app`.
6. افحص: `https://xxxx.up.railway.app/health` يعطي `{"status":"ok"}`، و`/v1/rooms` بلا رمز يعطي 401.
7. في GitHub ← Settings ← Secrets and variables ← Actions ← Variables: `VOICEUP_API_URL` = ذلك العنوان (بدون `/` في النهاية). أعد تشغيل Android CI ثم ثبّت الـ APK الجديد.
8. منح عملات للتجربة: من Railway افتح Postgres ← Variables وانسخ `DATABASE_PUBLIC_URL`، ثم من جهازك داخل `backend`:
   `DATABASE_URL="<الرابط>" npm run grant -- <uid> 1000 "test"`
