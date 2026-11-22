-- المرحلة 2: الغرف الصوتية، المقاعد، طلبات التحدث، الأدوار، الحظر
-- المعرّف uid هو معرّف Firebase Auth (نص)، لا يوجد جدول مستخدمين هنا بعد.

CREATE TABLE rooms (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  owner_uid   TEXT NOT NULL,
  title       TEXT NOT NULL CHECK (char_length(title) BETWEEN 3 AND 60),
  topic       TEXT CHECK (topic IS NULL OR char_length(topic) <= 30),
  status      TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open','closed')),
  max_seats   INT  NOT NULL DEFAULT 8 CHECK (max_seats BETWEEN 2 AND 16),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  closed_at   TIMESTAMPTZ
);

-- غرفة مفتوحة واحدة لكل مالك (ضبط إساءة الاستخدام، ويمنع سباق الإنشاء المزدوج)
CREATE UNIQUE INDEX rooms_one_open_per_owner ON rooms(owner_uid) WHERE status = 'open';
CREATE INDEX rooms_open_idx ON rooms(created_at DESC) WHERE status = 'open';

CREATE TABLE room_roles (
  room_id  UUID NOT NULL REFERENCES rooms(id) ON DELETE CASCADE,
  uid      TEXT NOT NULL,
  role     TEXT NOT NULL CHECK (role IN ('owner','moderator')),
  PRIMARY KEY (room_id, uid)
);

CREATE TABLE room_seats (
  room_id  UUID NOT NULL REFERENCES rooms(id) ON DELETE CASCADE,
  seat_no  INT  NOT NULL CHECK (seat_no >= 0),
  uid      TEXT NOT NULL,
  seated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (room_id, seat_no),
  UNIQUE (room_id, uid)
);

CREATE TABLE seat_requests (
  id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  room_id     UUID NOT NULL REFERENCES rooms(id) ON DELETE CASCADE,
  uid         TEXT NOT NULL,
  status      TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','approved','rejected')),
  decided_by  TEXT,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
  decided_at  TIMESTAMPTZ
);
CREATE UNIQUE INDEX seat_requests_one_pending ON seat_requests(room_id, uid) WHERE status = 'pending';

CREATE TABLE room_bans (
  room_id    UUID NOT NULL REFERENCES rooms(id) ON DELETE CASCADE,
  uid        TEXT NOT NULL,
  banned_by  TEXT NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (room_id, uid)
);
