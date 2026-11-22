-- المرحلة 0: الدفتر المحاسبي (قيد مزدوج، Append-only، NUMERIC فقط)

CREATE TABLE ledger_accounts (
  id          BIGSERIAL PRIMARY KEY,
  code        TEXT NOT NULL UNIQUE,
  name        TEXT NOT NULL,
  -- نوع الحساب يفصل بين المفاهيم الخمسة في المواصفة
  kind        TEXT NOT NULL CHECK (kind IN (
                'wallet_points',      -- رصيد افتراضي
                'gift_value',         -- قيمة الهدايا
                'revenue',            -- إيراد محاسبي
                'payable',            -- مستحقات
                'platform',           -- حصة المنصة / أرباح
                'fees',               -- رسوم المتجر والدفع
                'tax',                -- ضرائب مستبعدة
                'reward_pool'         -- صندوق المضيفات
              )),
  currency    TEXT NOT NULL CHECK (char_length(currency) BETWEEN 3 AND 8),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE ledger_transactions (
  id               BIGSERIAL PRIMARY KEY,
  idempotency_key  TEXT NOT NULL UNIQUE,
  type             TEXT NOT NULL CHECK (type IN (
                     'points_purchase','gift_sent','wallet_debit','eligible_revenue',
                     'agency_commission','reward_pool','refund','reward_payable','settlement')),
  rule_version_id  BIGINT,                -- يربط الحساب بإصدار القواعد المثبّت للفترة
  reverses_id      BIGINT REFERENCES ledger_transactions(id),
  metadata         JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE ledger_entries (
  id              BIGSERIAL PRIMARY KEY,
  transaction_id  BIGINT NOT NULL REFERENCES ledger_transactions(id),
  account_id      BIGINT NOT NULL REFERENCES ledger_accounts(id),
  direction       TEXT NOT NULL CHECK (direction IN ('debit','credit')),
  amount          NUMERIC(20,6) NOT NULL CHECK (amount > 0),
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ledger_entries_tx_idx ON ledger_entries(transaction_id);
CREATE INDEX ledger_entries_account_idx ON ledger_entries(account_id);

-- منع التعديل والحذف المباشر
CREATE FUNCTION ledger_block_mutation() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION 'ledger is append-only: % on % is not allowed', TG_OP, TG_TABLE_NAME;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER ledger_transactions_immutable
  BEFORE UPDATE OR DELETE ON ledger_transactions
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();

CREATE TRIGGER ledger_entries_immutable
  BEFORE UPDATE OR DELETE ON ledger_entries
  FOR EACH ROW EXECUTE FUNCTION ledger_block_mutation();

-- توازن كل معاملة: مجموع المدين = مجموع الدائن (يُفحص عند الإيداع)
CREATE FUNCTION ledger_check_balanced() RETURNS trigger AS $$
DECLARE
  d NUMERIC; c NUMERIC;
BEGIN
  SELECT COALESCE(SUM(amount) FILTER (WHERE direction = 'debit'), 0),
         COALESCE(SUM(amount) FILTER (WHERE direction = 'credit'), 0)
    INTO d, c
    FROM ledger_entries WHERE transaction_id = NEW.transaction_id;
  IF d <> c THEN
    RAISE EXCEPTION 'unbalanced ledger transaction %: debit % <> credit %', NEW.transaction_id, d, c;
  END IF;
  RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE CONSTRAINT TRIGGER ledger_entries_balanced
  AFTER INSERT ON ledger_entries
  DEFERRABLE INITIALLY DEFERRED
  FOR EACH ROW EXECUTE FUNCTION ledger_check_balanced();
