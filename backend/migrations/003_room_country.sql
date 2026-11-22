-- المرحلة 3: دولة الغرفة (للتصفية في الرئيسية). اختيارية، وتُتحقق قيمتها في الخادم من قائمة الدول المدعومة.
ALTER TABLE rooms ADD COLUMN country CHAR(2) CHECK (country IS NULL OR country ~ '^[A-Z]{2}$');
CREATE INDEX rooms_open_country_idx ON rooms(country, created_at DESC) WHERE status = 'open';
