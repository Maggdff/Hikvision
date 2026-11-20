#pragma once
// ============ إعدادات الصندوق (حرّرها حسب العتاد؛ لا أسرار هنا) ============
#define FW_VERSION "0.1.0"

#ifndef USE_ETH_W5500
#define USE_ETH_W5500 1          // 1 = Ethernet W5500 (موصى به)، 0 = Wi-Fi
#endif

// ---- الأطراف (ESP32-DevKitC الكلاسيكي) — راجعها على اللوحة الفعلية
#define PIN_LOCK      25         // بوابة MOSFET القفل (عبر 220Ω، مع سحب للأسفل 10k)
#define PIN_LED_G     26
#define PIN_LED_R     27
#define PIN_BUZZ      32
#define PIN_SDA       21         // PN532 (I2C)
#define PIN_SCL       22
#define PIN_PN532_IRQ 17
#define PIN_PN532_RST 14
// W5500 (SPI)
#define PIN_ETH_SCK   18
#define PIN_ETH_MISO  19
#define PIN_ETH_MOSI  23
#define PIN_ETH_CS     5
#define PIN_ETH_RST   16
#define PIN_ETH_INT   -1         // -1 = استطلاع (polling)

// ---- منطق القفل. A) مزلاج كهربائي (fail-secure): يُفتح بالتغذية.  B) قفل مغناطيسي (fail-safe): يُقفل بالتغذية.
#define LOCK_TYPE_STRIKE 1       // 1 = A (موصى به)، 0 = B
#if LOCK_TYPE_STRIKE
  #define LOCK_LOCKED_LEVEL   LOW
  #define LOCK_UNLOCKED_LEVEL HIGH
#else
  #define LOCK_LOCKED_LEVEL   HIGH
  #define LOCK_UNLOCKED_LEVEL LOW
#endif

// ---- التوقيتات
#define HEARTBEAT_MS        5000
#define HTTP_CONNECT_MS     3000
#define HTTP_TIMEOUT_MS     4000
#define CARD_DEBOUNCE_MS    3000
#define DEFAULT_LEASE_MS    120000UL   // إيجار الفتح الافتراضي بعد أمر فتح (يجدّده الجسر مع كل نبضة)
#define MAX_UNLOCK_MS       1200000UL  // سقف محلي مستقل عن الجسر: 20 دقيقة ثم يُقفل الجهاز
#define READER_RETRY_MS     30000
#define WDT_SECONDS         10
#define SEQ_JUMP_ON_BOOT    64         // يقفز التسلسل بعد كل إقلاع كي لا يُرفض (تسلسل أقدم)
#define SEQ_SAVE_EVERY      16
#define EVENTS_MAX_BYTES    16000
