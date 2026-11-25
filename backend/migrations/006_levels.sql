-- المرحلة 5 (تابع): مستويات الثروة والجاذبية، تُحسب من سجل الهدايا الحقيقي (لا تُخزَّن ولا تُعدَّل يدوياً).
-- العتبات بيانات قابلة للتعديل من قاعدة البيانات، وليست ثوابت في الكود.
CREATE TABLE level_thresholds (
  kind       TEXT   NOT NULL CHECK (kind IN ('wealth','charm')),
  level      INT    NOT NULL CHECK (level BETWEEN 1 AND 150),
  min_points BIGINT NOT NULL CHECK (min_points >= 0),
  PRIMARY KEY (kind, level)
);

-- بذرة أولية: نقاط المستوى n = 50 × (n−1)². المستوى 1 بلا نقاط. تُعدَّل لاحقاً حسب اقتصاد التطبيق.
INSERT INTO level_thresholds(kind, level, min_points)
SELECT k.kind, n, 50 * (n - 1) * (n - 1)
  FROM (VALUES ('wealth'), ('charm')) AS k(kind), generate_series(1, 150) AS n;

CREATE INDEX room_gifts_sender_idx    ON room_gifts(sender_uid);
CREATE INDEX room_gifts_recipient_idx ON room_gifts(recipient_uid);
