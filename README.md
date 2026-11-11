# Smart Water Monitor — نسخة Windows (.NET 8)

المرحلة 1 + الترخيص (من 3). المستودع الأصلي (أندرويد) لم يُلمس.

| المشروع | المحتوى | الحالة |
|---|---|---|
| `src/SmartWater.Core` | نقل حرفي لـ `GrayFrame` / `HandProfile` / `ShakeCompensator` / `MeterTracker` / `SwingEngine` / `FillSession` | مكتوب، لم يُبنَ بعد |
| `src/SmartWater.Engine` | `Roi` + `Pipeline` (OpenCvSharp) + لقطات ISAPI (`IsapiSnapshotClient`, `SnapshotFrameSource`) | مكتوب، لم يُجرَّب مع كاميرا |
| `src/SmartWater.Licensing` | الترخيص: `LicenseCodec` / `LicenseGate` / `TrustedClock` / `LicenseKeeper` / `LicenseApi` (Firebase REST) / `LicenseManager` | مكتوب، لم يُبنَ بعد |
| `tests/SmartWater.Tests` | اختبارات xUnit على بيانات اصطناعية | مكتوب، لم يُشغَّل بعد |

## التشغيل
```
dotnet test tests/SmartWater.Tests/SmartWater.Tests.csproj
```
أو ادفع المجلد `SmartWaterMonitor.Windows` إلى مستودعك، وانسخ `windows.yml` إلى `.github/workflows/` في **جذر** المستودع (GitHub لا يقرأ workflows من مجلد فرعي)؛ يبني Engine ويشغّل الاختبارات.

## المراحل القادمة
2. نقل `TruckMatcher` / `TruckPresence` / SQLite (`Store`, `TruckDb`, `SwingDb`) وربط `MonitorEngine`.
3. خدمة ويندوز (Worker Service) + واجهة WPF عربية (RTL) تستعمل `LicenseManager`.

## الترخيص
نفس صيغة المستند ومسارات Firebase (`swm/lic`, `swm/bind`) كنسخة أندرويد، فتعمل `devconsole/swm-license-console.html` كما هي: المفاتيح نفسها تفعّل نسخة ويندوز أيضاً، وكل كمبيوتر يُحسب «جهازاً» ضمن حد الأجهزة. الخدمة تتحقق كل 20 دقيقة كحد أدنى، وعند الحجب تتوقف عن الاتصال بالكاميرا والعدّ ويبقى السجل. القيمة `LicenseOptions.Required=false` لبناء التطوير الداخلي.

**لم يُنقل فحص بصمة التوقيع** (`Integrity.kt`): لا مقابل مباشر له في .NET، وملفات .NET يسهل فكها. الحماية هنا تمنع الاستخدام العادي وتطبّق التعليق والانتهاء فقط. لرفع الكلفة لاحقاً: توقيع Authenticode للملف التنفيذي + تشويش الشيفرة (obfuscation) + إبقاء المستودع خاصاً.
