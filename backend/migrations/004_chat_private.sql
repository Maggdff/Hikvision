-- المرحلة 4: دردشة الغرفة، الغرف الخاصة (كلمة سر من 6 أرقام)، وسجل الدخول والمحاولات الفاشلة

ALTER TABLE rooms ADD COLUMN password_hash TEXT;  -- null = غرفة عامة؛ الصيغة scrypt$salt$hash

-- من دخل الغرفة بنجاح (بكلمة السر إن لزمت). يُشترط للدردشة، وللاطلاع على الغرف الخاصة.
CREATE TABLE room_access (
  room_id    UUID NOT NULL REFERENCES rooms(id) ON DELETE CASCADE,
  uid        TEXT NOT NULL,
  granted_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (room_id, uid)
);

-- سجل المحاولات الفاشلة: يحدّ من تخمين كلمة السر لكل مستخدم ولكل غرفة
CREATE TABLE room_password_failures (
  id      BIGSERIAL PRIMARY KEY,
  room_id UUID NOT NULL REFERENCES rooms(id) ON DELETE CASCADE,
  uid     TEXT NOT NULL,
  at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX room_pw_failures_room_idx ON room_password_failures(room_id, at DESC);
CREATE INDEX room_pw_failures_user_idx ON room_password_failures(room_id, uid, at DESC);

CREATE TABLE room_messages (
  id         BIGSERIAL PRIMARY KEY,
  room_id    UUID NOT NULL REFERENCES rooms(id) ON DELETE CASCADE,
  uid        TEXT NOT NULL,
  body       TEXT NOT NULL CHECK (char_length(body) BETWEEN 1 AND 300),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX room_messages_room_idx ON room_messages(room_id, id);
