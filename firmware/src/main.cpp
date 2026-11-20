// WaterGate box firmware — ESP32 + PN532 (NFC, I2C) + W5500 (Ethernet) + lock driver.
// STATUS: written against the bridge protocol (src/SmartWater.Gate). NOT compiled or tested on hardware yet.
//
// Behaviour (see firmware/README.md):
//  - Every request to the PC is HMAC-SHA256 signed (same canonical string as GateAuth.cs).
//  - The card decision comes back in the /api/access/check response; no local allow ever.
//  - Lock output is safe by default and follows the bridge's desired state + a rolling lease;
//    if the bridge stops renewing the lease (or time is unknown) the box returns to the safe state.
//  - Offline events are kept in LittleFS and uploaded later (de-duplicated by device+seq on the server).
#include <Arduino.h>
#include <Preferences.h>
#include <LittleFS.h>
#include <HTTPClient.h>
#include <ArduinoJson.h>
#include <Wire.h>
#include <Adafruit_PN532.h>
#include <esp_task_wdt.h>
#include <esp_timer.h>
#include <esp_system.h>
#include <vector>
#include "mbedtls/md.h"
#include "mbedtls/sha256.h"
#include "config.h"
#if USE_ETH_W5500
  #include <ETH.h>
  #include <SPI.h>
#else
  #include <WiFi.h>
#endif

// ---------------------------------------------------------------- state
struct Cfg { String deviceId, keyHex, host, ssid, pass; uint16_t port = 8080; } cfg;
static uint8_t gKey[32];
static bool gKeyOk = false;
static Preferences prefs;
static Adafruit_PN532 nfc(PIN_PN532_IRQ, PIN_PN532_RST);

static bool gUnlocked = false;
static int64_t gUnlockedAt = 0, gLeaseUntil = 0;
static uint32_t gAppliedVer = 0, gLastDesiredVer = 0, gIgnoreVer = 0;
static bool gOnline = false, gReaderOk = false;
static int64_t gOffset = 0;
static bool gTimeOk = false;
static uint32_t gSeq = 0, gSeqSaved = 0, gEvSeq = 0;
static int64_t gNextHb = 0, gNextHealth = 0, gReaderRetryAt = 0, gLastUidAt = 0;
static String gLastUid;

// ---------------------------------------------------------------- helpers
static int64_t nowMs() { return esp_timer_get_time() / 1000; }
static int64_t serverNow() { return nowMs() + gOffset; }
static void syncTime(int64_t serverMs) { gOffset = serverMs - nowMs(); gTimeOk = true; }
static void kick() { esp_task_wdt_reset(); }

static String toHex(const uint8_t* d, size_t n) {
  static const char* H = "0123456789abcdef";
  String s; s.reserve(n * 2);
  for (size_t i = 0; i < n; i++) { s += H[d[i] >> 4]; s += H[d[i] & 15]; }
  return s;
}
static int hexv(char c) {
  if (c >= '0' && c <= '9') return c - '0';
  if (c >= 'a' && c <= 'f') return c - 'a' + 10;
  if (c >= 'A' && c <= 'F') return c - 'A' + 10;
  return -1;
}
static bool fromHex(const String& h, uint8_t* out, size_t n) {
  if (h.length() != n * 2) return false;
  for (size_t i = 0; i < n; i++) {
    int a = hexv(h[2 * i]), b = hexv(h[2 * i + 1]);
    if (a < 0 || b < 0) return false;
    out[i] = (uint8_t)((a << 4) | b);
  }
  return true;
}

// ---------------------------------------------------------------- crypto (matches GateAuth.cs)
static String sha256Hex(const uint8_t* d, size_t n) {
  uint8_t out[32];
  mbedtls_sha256_context c;
  mbedtls_sha256_init(&c);
  mbedtls_sha256_starts(&c, 0);
  mbedtls_sha256_update(&c, d, n);
  mbedtls_sha256_finish(&c, out);
  mbedtls_sha256_free(&c);
  return toHex(out, 32);
}
static String hmacHex(const uint8_t* key, size_t kl, const String& msg) {
  uint8_t out[32];
  const mbedtls_md_info_t* info = mbedtls_md_info_from_type(MBEDTLS_MD_SHA256);
  mbedtls_md_hmac(info, key, kl, (const uint8_t*)msg.c_str(), msg.length(), out);
  return toHex(out, 32);
}
static String canonical(const char* method, const String& path, const String& dev, uint32_t seq, int64_t ts,
                        const String& nonce, const String& body) {
  char tsb[24]; snprintf(tsb, sizeof tsb, "%lld", (long long)ts);
  String s;
  s += method; s += "\n"; s += path; s += "\n"; s += dev; s += "\n";
  s += String((unsigned long)seq); s += "\n"; s += tsb; s += "\n"; s += nonce; s += "\n";
  s += sha256Hex((const uint8_t*)body.c_str(), body.length());
  return s;
}
static String makeNonce() {
  uint8_t b[16];
  for (int i = 0; i < 16; i += 4) { uint32_t r = esp_random(); memcpy(b + i, &r, 4); }
  return toHex(b, 16);
}
static uint32_t nextSeq() {
  gSeq++;
  if (gSeq - gSeqSaved >= SEQ_SAVE_EVERY) { prefs.putUInt("seq", gSeq); gSeqSaved = gSeq; }
  return gSeq;
}

// ---------------------------------------------------------------- network
static bool netUp() {
#if USE_ETH_W5500
  return ETH.linkUp() && ETH.hasIP();
#else
  return WiFi.status() == WL_CONNECTED;
#endif
}
static void netBegin() {
#if USE_ETH_W5500
  SPI.begin(PIN_ETH_SCK, PIN_ETH_MISO, PIN_ETH_MOSI);
  ETH.begin(ETH_PHY_W5500, 1, PIN_ETH_CS, PIN_ETH_INT, PIN_ETH_RST, SPI);   // Arduino-ESP32 core 3.x signature (verify on your core)
#else
  WiFi.mode(WIFI_STA);
  WiFi.begin(cfg.ssid.c_str(), cfg.pass.c_str());
#endif
}

// ---------------------------------------------------------------- offline events (LittleFS)
static void addEvent(const char* type, const String& data) {
  if (LittleFS.exists("/ev.jsonl")) {
    File t = LittleFS.open("/ev.jsonl", "r");
    size_t sz = t ? t.size() : 0; if (t) t.close();
    if (sz > EVENTS_MAX_BYTES) LittleFS.remove("/ev.jsonl");          // الأقدم يُسقَط عند الامتلاء
  }
  File f = LittleFS.open("/ev.jsonl", "a");
  if (!f) return;
  gEvSeq++; prefs.putUInt("evseq", gEvSeq);
  JsonDocument d;
  d["seq"] = gEvSeq; d["ts"] = (long long)serverNow(); d["type"] = type; d["data"] = data;
  serializeJson(d, f); f.print("\n"); f.close();
}

// ---------------------------------------------------------------- signed POST
static bool post(const char* path, const String& body, JsonDocument& out, int& code) {
  code = -1;
  if (!netUp() || !gKeyOk || cfg.host.isEmpty() || cfg.deviceId.isEmpty()) return false;
  for (int attempt = 0; attempt < 2; attempt++) {
    kick();
    HTTPClient http;
    http.setConnectTimeout(HTTP_CONNECT_MS);
    http.setTimeout(HTTP_TIMEOUT_MS);
    String url = String("http://") + cfg.host + ":" + String(cfg.port) + path;
    if (!http.begin(url)) { code = -2; return false; }
    uint32_t seq = nextSeq();
    int64_t ts = gTimeOk ? serverNow() : 0;            // قبل أول مزامنة: يردّ الخادم StaleTime ومعه وقته
    String nn = makeNonce();
    String sig = hmacHex(gKey, 32, canonical("POST", path, cfg.deviceId, seq, ts, nn, body));
    char tsb[24]; snprintf(tsb, sizeof tsb, "%lld", (long long)ts);
    http.addHeader("Content-Type", "application/json");
    http.addHeader("X-Device", cfg.deviceId);
    http.addHeader("X-Seq", String((unsigned long)seq));
    http.addHeader("X-Ts", tsb);
    http.addHeader("X-Nonce", nn);
    http.addHeader("X-Sig", sig);
    code = http.POST((uint8_t*)body.c_str(), body.length());
    String resp = code > 0 ? http.getString() : String("");
    http.end();
    if (code <= 0) return false;
    out.clear();
    deserializeJson(out, resp);
    if (out["serverTimeMs"].is<long long>()) syncTime(out["serverTimeMs"].as<long long>());
    const char* err = out["error"] | "";
    if (code == 401 && attempt == 0 && !strcmp(err, "StaleTime") && gTimeOk) continue;   // أعد المحاولة بالوقت المضبوط
    return code == 200;
  }
  return false;
}

// ---------------------------------------------------------------- lock
static void setLock(bool unlock) {
  if (unlock == gUnlocked) return;
  gUnlocked = unlock;
  digitalWrite(PIN_LOCK, unlock ? LOCK_UNLOCKED_LEVEL : LOCK_LOCKED_LEVEL);
  if (unlock) gUnlockedAt = nowMs();
  addEvent(unlock ? "unlocked" : "locked", "");
}

static void enforceLock() {
  if (!gUnlocked) return;
  if (!gTimeOk || serverNow() > gLeaseUntil) { setLock(false); addEvent("lease_expired", ""); return; }
  if (nowMs() - gUnlockedAt > (int64_t)MAX_UNLOCK_MS) { gIgnoreVer = gLastDesiredVer; setLock(false); addEvent("max_unlock", ""); }
}

// ---------------------------------------------------------------- feedback (LED + buzzer)
enum Fb { FB_OK, FB_DENY, FB_NOLINK, FB_LEARN };
static void beep(int ms) { digitalWrite(PIN_BUZZ, HIGH); delay(ms); digitalWrite(PIN_BUZZ, LOW); }
static void feedback(Fb f) {
  switch (f) {
    case FB_OK:     digitalWrite(PIN_LED_G, HIGH); beep(120); delay(350); digitalWrite(PIN_LED_G, LOW); break;
    case FB_DENY:   for (int i = 0; i < 2; i++) { digitalWrite(PIN_LED_R, HIGH); beep(150); delay(120); digitalWrite(PIN_LED_R, LOW); delay(120); } break;
    case FB_NOLINK: for (int i = 0; i < 3; i++) { digitalWrite(PIN_LED_R, HIGH); beep(60); delay(80); digitalWrite(PIN_LED_R, LOW); delay(80); } break;
    case FB_LEARN:  for (int i = 0; i < 2; i++) { digitalWrite(PIN_LED_G, HIGH); delay(120); digitalWrite(PIN_LED_G, LOW); delay(120); } break;
  }
}

// ---------------------------------------------------------------- protocol
static void sendAck(const char* cmdId, bool executed) {
  JsonDocument rq; rq["cmdId"] = cmdId; rq["executed"] = executed; rq["lockState"] = gUnlocked ? "unlocked" : "locked";
  String body; serializeJson(rq, body);
  JsonDocument rs; int code;
  post("/api/access/ack", body, rs, code);        // فشل الإرسال لا يضر: ستعيد النبضة التالية الأمر فنؤكده ثانية
}

static void runCommand(JsonObject c) {
  const char* id = c["cmdId"] | "";
  const char* type = c["type"] | "";
  long long exp = c["expiresMs"] | 0LL;
  uint32_t ver = c["version"] | 0;
  if (!gTimeOk || serverNow() > exp) { addEvent("cmd_expired", id); return; }   // لا أوامر قديمة
  if (ver < gAppliedVer) { addEvent("cmd_stale", id); return; }
  if (!strcmp(type, "unlock")) { setLock(true); gLeaseUntil = serverNow() + DEFAULT_LEASE_MS; }
  else if (!strcmp(type, "lock")) setLock(false);
  else return;
  gAppliedVer = ver;
  sendAck(id, true);
}

static void applyDesired(JsonObject d) {
  if (d.isNull()) return;
  const char* st = d["state"] | "";
  uint32_t ver = d["version"] | 0;
  long long lease = d["leaseUntilMs"] | 0LL;
  gLastDesiredVer = ver;
  if (!strcmp(st, "Unlocked")) {
    if (gTimeOk && serverNow() < lease && ver >= gAppliedVer && ver != gIgnoreVer) { setLock(true); gLeaseUntil = lease; gAppliedVer = ver; }
    else if (gUnlocked && (!gTimeOk || serverNow() >= lease)) setLock(false);
  } else if (!strcmp(st, "Locked")) {
    if (ver >= gAppliedVer) { setLock(false); gAppliedVer = ver; }
  }
}

static void flushEvents() {
  if (!LittleFS.exists("/ev.jsonl")) return;
  File f = LittleFS.open("/ev.jsonl", "r");
  if (!f) return;
  std::vector<String> lines;
  while (f.available()) { String l = f.readStringUntil('\n'); l.trim(); if (l.length()) lines.push_back(l); }
  f.close();
  if (lines.empty()) { LittleFS.remove("/ev.jsonl"); return; }
  size_t n = lines.size() < 20 ? lines.size() : 20;
  String body = "{\"events\":[";
  for (size_t i = 0; i < n; i++) { if (i) body += ","; body += lines[i]; }
  body += "]}";
  JsonDocument rs; int code;
  if (!post("/api/device/events", body, rs, code) || code != 200) return;
  if (n >= lines.size()) LittleFS.remove("/ev.jsonl");
  else {
    File w = LittleFS.open("/ev.jsonl", "w");
    for (size_t i = n; i < lines.size(); i++) { w.print(lines[i]); w.print("\n"); }
    w.close();
  }
}

static void doHeartbeat() {
  JsonDocument rq; rq["lockState"] = gUnlocked ? "unlocked" : "locked"; rq["readerOk"] = gReaderOk; rq["fw"] = FW_VERSION;
  String body; serializeJson(rq, body);
  JsonDocument rs; int code;
  if (!post("/api/device/heartbeat", body, rs, code) || code != 200) { gOnline = false; return; }
  gOnline = true;
  applyDesired(rs["desired"].as<JsonObject>());
  JsonArray cmds = rs["commands"].as<JsonArray>();
  for (JsonObject c : cmds) runCommand(c);
  flushEvents();
}

static void onCard(const String& uid) {
  JsonDocument rq; rq["uid"] = uid;
  String body; serializeJson(rq, body);
  JsonDocument rs; int code;
  if (!post("/api/access/check", body, rs, code) || code != 200) { feedback(FB_NOLINK); addEvent("tap_offline", ""); return; }   // لا يقرّر الجهاز محليًا أبدًا
  if (!(rs["allowed"] | false)) { const char* r = rs["reason"] | ""; feedback(!strcmp(r, "learning") ? FB_LEARN : FB_DENY); return; }
  JsonObject c = rs["command"].as<JsonObject>();
  if (!c.isNull()) runCommand(c);
  feedback(FB_OK);
}

// ---------------------------------------------------------------- NFC reader
static void readerInit() {
  nfc.begin();
  gReaderOk = nfc.getFirmwareVersion() != 0;
  if (gReaderOk) nfc.SAMConfig(); else addEvent("reader_error", "");
}

static void pollCard() {
  if (!gReaderOk) { if (nowMs() >= gReaderRetryAt) { gReaderRetryAt = nowMs() + READER_RETRY_MS; readerInit(); } return; }
  uint8_t uid[7]; uint8_t len = 0;
  if (!nfc.readPassiveTargetID(PN532_MIFARE_ISO14443A, uid, &len, 50)) return;
  String hex = toHex(uid, len); hex.toUpperCase();
  if (hex == gLastUid && nowMs() - gLastUidAt < CARD_DEBOUNCE_MS) return;
  gLastUid = hex; gLastUidAt = nowMs();
  onCard(hex);
}

// ---------------------------------------------------------------- serial provisioning
static void selfTest() {
  uint8_t key[32]; for (int i = 0; i < 32; i++) key[i] = (uint8_t)i;
  String body = "{\"uid\":\"04A1B2C3\"}";
  String sig = hmacHex(key, 32, canonical("POST", "/api/access/check", "ESP1", 7, 1800000000123LL, "0123456789abcdef", body));
  const char* expected = "698a6bfb075693abd6a7e1553ef3dba3bbee17b293023dbf3e89584c633ae08f";
  Serial.println(sig == expected ? "SELFTEST OK (signature matches the server reference)" : "SELFTEST FAIL " + sig);
}

static void printStatus() {
  Serial.printf("fw=%s device=%s host=%s:%u key=%s net=%s online=%d reader=%d lock=%s timeSynced=%d seq=%lu\n",
                FW_VERSION, cfg.deviceId.c_str(), cfg.host.c_str(), cfg.port, gKeyOk ? "set" : "MISSING",
                netUp() ? "up" : "down", gOnline, gReaderOk, gUnlocked ? "unlocked" : "locked", gTimeOk, (unsigned long)gSeq);
}

static void runSerial(String l) {
  l.trim();
  int sp = l.indexOf(' ');
  String cmd = sp < 0 ? l : l.substring(0, sp);
  String arg = sp < 0 ? String("") : l.substring(sp + 1);
  arg.trim(); cmd.toUpperCase();
  if (cmd == "SET") {
    int s2 = arg.indexOf(' ');
    String k = s2 < 0 ? arg : arg.substring(0, s2);
    String v = s2 < 0 ? String("") : arg.substring(s2 + 1);
    v.trim(); k.toLowerCase();
    if (k == "deviceid" && v.length()) { cfg.deviceId = v; prefs.putString("dev", v); Serial.println("OK"); }
    else if (k == "key") {
      uint8_t tmp[32];
      if (fromHex(v, tmp, 32)) { memcpy(gKey, tmp, 32); gKeyOk = true; prefs.putString("key", v); Serial.println("OK"); }
      else Serial.println("ERR key must be 64 hex chars");
    }
    else if (k == "host" && v.length()) { cfg.host = v; prefs.putString("host", v); Serial.println("OK"); }
    else if (k == "port") { long p = v.toInt(); if (p > 0 && p < 65536) { cfg.port = (uint16_t)p; prefs.putUShort("port", cfg.port); Serial.println("OK"); } else Serial.println("ERR port"); }
    else if (k == "wifi") {
      int s3 = v.indexOf(' ');
      if (s3 < 0) { Serial.println("ERR usage: SET wifi <ssid> <password>"); return; }
      cfg.ssid = v.substring(0, s3); cfg.pass = v.substring(s3 + 1);
      prefs.putString("ssid", cfg.ssid); prefs.putString("pass", cfg.pass); Serial.println("OK (reboot to apply)");
    }
    else Serial.println("ERR unknown setting");
  }
  else if (cmd == "STATUS") printStatus();
  else if (cmd == "SELFTEST") selfTest();
  else if (cmd == "REBOOT") ESP.restart();
  else if (cmd == "CLEAR") { prefs.clear(); Serial.println("cleared — reboot"); }
  else Serial.println("commands: SET deviceid|key|host|port|wifi ..., STATUS, SELFTEST, REBOOT, CLEAR");
}

static void handleSerial() {
  static String line;
  while (Serial.available()) {
    char ch = (char)Serial.read();
    if (ch == '\n' || ch == '\r') { if (line.length()) runSerial(line); line = ""; }
    else if (line.length() < 200) line += ch;
  }
}

// ---------------------------------------------------------------- Arduino entry points
void setup() {
  pinMode(PIN_LOCK, OUTPUT);
  digitalWrite(PIN_LOCK, LOCK_LOCKED_LEVEL);            // الحالة الآمنة أولًا قبل أي شيء
  pinMode(PIN_LED_G, OUTPUT); pinMode(PIN_LED_R, OUTPUT); pinMode(PIN_BUZZ, OUTPUT);
  Serial.begin(115200);
#if ESP_ARDUINO_VERSION_MAJOR >= 3
  esp_task_wdt_config_t wc = { .timeout_ms = (uint32_t)WDT_SECONDS * 1000, .idle_core_mask = 0, .trigger_panic = true };
  esp_task_wdt_reconfigure(&wc);
#else
  esp_task_wdt_init(WDT_SECONDS, true);
#endif
  esp_task_wdt_add(NULL);

  LittleFS.begin(true);
  prefs.begin("gate", false);
  cfg.deviceId = prefs.getString("dev", "");
  cfg.host = prefs.getString("host", "");
  cfg.port = prefs.getUShort("port", 8080);
  cfg.ssid = prefs.getString("ssid", "");
  cfg.pass = prefs.getString("pass", "");
  cfg.keyHex = prefs.getString("key", "");
  gKeyOk = fromHex(cfg.keyHex, gKey, 32);
  gSeq = prefs.getUInt("seq", 0) + SEQ_JUMP_ON_BOOT;     // قفزة تضمن تسلسلًا أكبر من أي طلب سابق لم يُحفظ
  prefs.putUInt("seq", gSeq); gSeqSaved = gSeq;
  gEvSeq = prefs.getUInt("evseq", 0);

  Wire.begin(PIN_SDA, PIN_SCL);
  readerInit();
  netBegin();
  addEvent("boot", FW_VERSION);
  Serial.println("WaterGate box ready. Type STATUS or SELFTEST.");
}

void loop() {
  kick();
  handleSerial();
  enforceLock();
  pollCard();
  int64_t t = nowMs();
  if (t >= gNextHb) { gNextHb = t + HEARTBEAT_MS; doHeartbeat(); }
  if (t >= gNextHealth) {
    gNextHealth = t + READER_RETRY_MS;
    if (gReaderOk && nfc.getFirmwareVersion() == 0) { gReaderOk = false; addEvent("reader_error", ""); }
  }
  delay(5);
}
