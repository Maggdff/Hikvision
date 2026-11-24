-- المرحلة 5: محفظة العملات، كتالوج الهدايا التجميلية، إرسال الهدايا (عبر الدفتر المحاسبي)

-- نوع معاملة جديد للمنح الترويجية (يُسجَّل بالدفتر كأي حركة، لا رصيد يُكتب مباشرة)
ALTER TABLE ledger_transactions DROP CONSTRAINT ledger_transactions_type_check;
ALTER TABLE ledger_transactions ADD CONSTRAINT ledger_transactions_type_check CHECK (type IN (
  'points_purchase','gift_sent','wallet_debit','eligible_revenue',
  'agency_commission','reward_pool','refund','reward_payable','settlement','promo_grant'));

-- حسابات المنصة الثابتة. محافظ المستخدمين تُنشأ عند أول استخدام: wallet:<uid>
INSERT INTO ledger_accounts(code, name, kind, currency) VALUES
  ('platform:promo',      'Promotional grants source', 'platform',   'COIN'),
  ('platform:gift_value', 'Gift value consumed',       'gift_value', 'COIN');

-- كتالوج الهدايا: بيانات قابلة للتعديل (الأسعار ليست ثوابت في الكود)
CREATE TABLE gifts (
  id          TEXT PRIMARY KEY CHECK (id ~ '^[a-z0-9_]{2,32}$'),
  name_ar     TEXT NOT NULL,
  name_en     TEXT NOT NULL,
  price_coins INT  NOT NULL CHECK (price_coins > 0),
  tier        TEXT NOT NULL DEFAULT 'small' CHECK (tier IN ('small','medium','large')),
  active      BOOLEAN NOT NULL DEFAULT true,
  sort_order  INT NOT NULL DEFAULT 0,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO gifts(id, name_ar, name_en, price_coins, tier, sort_order) VALUES
  ('rose',    'وردة',        'Rose',         10,    'small',  1),
  ('heart',   'قلب',         'Heart',        50,    'small',  2),
  ('coffee',  'فنجان قهوة',  'Coffee',       100,   'small',  3),
  ('star',    'نجمة',        'Star',         200,   'medium', 4),
  ('crown',   'تاج',         'Crown',        500,   'medium', 5),
  ('rocket',  'صاروخ',       'Rocket',       1000,  'large',  6),
  ('car',     'سيارة',       'Car',          3000,  'large',  7),
  ('palace',  'قصر',         'Palace',       10000, 'large',  8);

-- سجل الهدايا المرسلة (يربط الهدية بمعاملة الدفتر)
CREATE TABLE room_gifts (
  id            BIGSERIAL PRIMARY KEY,
  room_id       UUID NOT NULL REFERENCES rooms(id) ON DELETE CASCADE,
  sender_uid    TEXT NOT NULL,
  recipient_uid TEXT NOT NULL,
  gift_id       TEXT NOT NULL REFERENCES gifts(id),
  price_coins   INT  NOT NULL,           -- السعر وقت الإرسال (لا يتأثر بتعديل الكتالوج لاحقاً)
  ledger_tx_id  BIGINT NOT NULL UNIQUE REFERENCES ledger_transactions(id),
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX room_gifts_room_idx ON room_gifts(room_id, id);

-- رسائل النظام (الهدايا) تظهر في نفس مجرى الدردشة
ALTER TABLE room_messages ADD COLUMN kind TEXT NOT NULL DEFAULT 'text' CHECK (kind IN ('text','gift'));
ALTER TABLE room_messages ADD COLUMN meta JSONB NOT NULL DEFAULT '{}'::jsonb;
