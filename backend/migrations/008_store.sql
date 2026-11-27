-- المرحلة 8: متجر الإطارات (SVGA) وهدية بوغاتي. الشراء يمر عبر الدفتر المحاسبي كأي حركة.

INSERT INTO ledger_accounts(code, name, kind, currency) VALUES
  ('platform:store_sales', 'Store purchases (frames)', 'platform', 'COIN');

INSERT INTO gifts(id, name_ar, name_en, price_coins, tier, sort_order) VALUES
  ('bugatti', 'بوغاتي', 'Bugatti', 20000, 'large', 9);

-- كتالوج الإطارات المتحركة. asset = اسم ملف SVGA داخل التطبيق (لا مسارات حرة من الخادم)
CREATE TABLE frames (
  id          TEXT PRIMARY KEY CHECK (id ~ '^[a-z0-9_]{2,32}$'),
  name_ar     TEXT NOT NULL,
  name_en     TEXT NOT NULL,
  price_coins INT  NOT NULL CHECK (price_coins > 0),
  active      BOOLEAN NOT NULL DEFAULT true,
  sort_order  INT NOT NULL DEFAULT 0,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO frames(id, name_ar, name_en, price_coins, sort_order) VALUES
  ('vip6', 'إطار VIP 6', 'VIP 6 Frame', 5000, 1);

CREATE TABLE user_frames (
  uid          TEXT NOT NULL,
  frame_id     TEXT NOT NULL REFERENCES frames(id),
  ledger_tx_id BIGINT NOT NULL REFERENCES ledger_transactions(id),
  acquired_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (uid, frame_id)
);

-- الإطار المفعّل؛ المفتاح الأجنبي المركّب يضمن أنه مملوك للمستخدم
CREATE TABLE user_equipped_frame (
  uid      TEXT PRIMARY KEY,
  frame_id TEXT NOT NULL,
  FOREIGN KEY (uid, frame_id) REFERENCES user_frames(uid, frame_id)
);
