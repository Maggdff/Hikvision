# متجر الإطارات وهدية بوغاتي (SVGA)

- **الملفات**: `app/assets/svga/vip6_frame.svga` (إطار VIP 6) و`bugatti_gift.svga` (تأثير الهدية)، و`app/assets/gifts/bugatti.webp` (أيقونتها).
  صُغّرت أبعاد صور الإطارات بـ `app/tool/optimize_svga.py` (الحركة والشفافية والمواقع كما هي) لأن فك ترميز النسخ الأصلية يستهلك مئات الميغابايت من الذاكرة.
- **الخادم**: الهجرة `008_store.sql` تضيف الهدية `bugatti` (20,000) والإطار `vip6` (5,000) وجداول الملكية. الشراء قيد مزدوج بالدفتر بمفتاح idempotency.
  المسارات: `GET /v1/store/frames`، `POST /v1/store/frames/:id/buy`، `PUT /v1/me/frame`، `GET /v1/me`.
- **العملات التجريبية**: زر «عملات تجريبية (50,000)» في شاشة «أنا» يظهر فقط للمشرفين، ويستخدم `POST /v1/admin/grants` الموجود.
  لتفعيله ضع معرّف حسابك (Firebase UID) في متغير Railway `ADMIN_UIDS` ثم أعد النشر.
- **ربط الحقول**: أضف أي إطار/هدية SVGA جديدة بإضافة صفها في قاعدة البيانات وملفها في `SvgaAssets` (`app/lib/ui/svga.dart`) بالمعرّف نفسه.
