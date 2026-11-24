# نشر الخادم (Cloud Run + PostgreSQL)

الحالة: الملفات جاهزة ومجرَّبة محلياً (الناتج المترجم يعمل مع PostgreSQL، والـ Migrations تُطبَّق مرة واحدة حتى مع نسخ متزامنة). **صورة Docker لم تُبنَ بعد** لعدم توفر Docker في بيئة التطوير؛ أول بناء لها يحدث في Backend CI على GitHub.

## 1) المتغيرات المطلوبة (أسرار، لا تُكتب في الكود ولا في المحادثة)

| المتغير | المصدر |
|---|---|
| `DATABASE_URL` | قاعدة PostgreSQL مُدارة (Cloud SQL أو Neon أو Supabase) |
| `LIVEKIT_URL` | لوحة LiveKit Cloud، بصيغة `wss://...livekit.cloud` |
| `LIVEKIT_API_KEY` / `LIVEKIT_API_SECRET` | لوحة LiveKit Cloud |
| `FIREBASE_PROJECT_ID` | `voice-up-84ee1` (ليس سراً) |
| `RUN_MIGRATIONS` | `true` لتطبيق الـ Migrations عند الإقلاع (محمي بقفل) |
| `PORT` | يضبطه Cloud Run تلقائياً |

التحقق من رموز Firebase للمستخدمين لا يحتاج ملف Service Account. ملف المفتاح الخاص لا يلزم في هذه المرحلة.

## 2) إعداد Google Cloud (مرة واحدة)

1. تفعيل الواجهات: Cloud Run، Artifact Registry، Secret Manager، Cloud Build.
2. إنشاء 4 أسرار في Secret Manager بهذه الأسماء بالضبط:
   `voiceup-database-url`، `voiceup-livekit-url`، `voiceup-livekit-api-key`، `voiceup-livekit-api-secret`.
3. منح حساب خدمة Cloud Run دور `Secret Manager Secret Accessor`.
4. إعداد Workload Identity Federation بين GitHub وحساب خدمة للنشر (بدل مفتاح JSON طويل العمر).
5. في GitHub: Secrets باسم `GCP_WORKLOAD_IDENTITY_PROVIDER` و`GCP_SERVICE_ACCOUNT`، وVariable باسم `GCP_REGION`.

## 3) النشر

Actions، ثم Deploy Backend (Cloud Run)، ثم Run workflow. النشر يدوي فقط عمداً.

## 4) بعد النشر

- فحص: `GET https://<service-url>/health` يجب أن يُرجع `{"status":"ok"}`.
- `GET /v1/rooms` بلا رمز يجب أن يُرجع 401.
- ضبط تنبيه ميزانية على المشروع، وحدّاً أقصى لعدد النسخ في Cloud Run لتفادي مفاجآت التكلفة.

## 5) ما ينقص قبل الإطلاق العام

- Webhooks من LiveKit (عدد المستمعين الحقيقي، ومزامنة عند فشل التحديث).
- تحديد معدل الطلبات (Rate limiting) وفلترة الإساءة.
- فرض التحقق العمري 18+ من الخادم، وهو شرط قبل أي مزايا مالية.
- نسخ احتياطي واختبار استعادة لقاعدة البيانات.


## متغير اختياري: مشرفو العملات

`ADMIN_UIDS` قائمة معرّفات Firebase مفصولة بفواصل، يسمح لأصحابها باستعمال `POST /v1/admin/grants` (منح عملات ترويجية مسجّلة في الدفتر).
اتركه فارغاً إن لم تحتجه؛ المنح عبر `npm run grant` يتطلب فقط الوصول إلى قاعدة البيانات.
