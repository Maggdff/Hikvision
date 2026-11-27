-- المتابعة (متابِع → متابَع) ولافتات الرئيسية التي تديرها الإدارة (فارغة افتراضياً؛ لا محتوى مختلق).
CREATE TABLE follows (
  follower_uid TEXT NOT NULL,
  followee_uid TEXT NOT NULL,
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (follower_uid, followee_uid),
  CHECK (follower_uid <> followee_uid)
);
CREATE INDEX follows_followee_idx ON follows(followee_uid);

CREATE TABLE banners (
  id         BIGSERIAL PRIMARY KEY,
  title      TEXT NOT NULL CHECK (char_length(title) BETWEEN 1 AND 80),
  subtitle   TEXT CHECK (subtitle IS NULL OR char_length(subtitle) <= 120),
  active     BOOLEAN NOT NULL DEFAULT true,
  starts_at  TIMESTAMPTZ,
  ends_at    TIMESTAMPTZ,
  sort_order INT NOT NULL DEFAULT 0
);
