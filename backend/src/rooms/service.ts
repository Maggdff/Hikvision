import pg from 'pg';
import { badRequest, conflict, forbidden, notFound, tooManyRequests } from '../errors.js';
import type { VoiceProvider } from '../voice.js';
import { isSupportedCountry } from './countries.js';
import { hashRoomPassword, isValidRoomPassword, verifyRoomPassword } from './password.js';

export type Role = 'owner' | 'moderator' | 'member';
const RANK: Record<Role, number> = { owner: 3, moderator: 2, member: 1 };

export interface RoomSummary {
  id: string;
  title: string;
  topic: string | null;
  ownerUid: string;
  seatedCount: number;
  maxSeats: number;
  country: string | null;
  isPrivate: boolean;
  createdAt: string;
}

export interface ChatMessage {
  id: number;
  uid: string;
  body: string;
  kind: 'text' | 'gift';
  meta: Record<string, unknown>;
  createdAt: string;
}

export interface SeatRequest {
  id: string;
  roomId: string;
  uid: string;
  status: string;
  createdAt: string;
}

/** نتيجة إجراء يغيّر حالة الصوت: voiceSynced=false تعني أن قاعدة البيانات تغيّرت وتعذّرت مزامنة LiveKit. */
export interface ActionResult {
  ok: true;
  voiceSynced: boolean;
}

export class RoomService {
  constructor(
    private readonly pool: pg.Pool,
    private readonly voice: VoiceProvider,
    private readonly onVoiceError: (e: unknown) => void = () => {},
  ) {}

  // ---------- أدوات داخلية ----------

  private async tx<T>(fn: (c: pg.PoolClient) => Promise<T>): Promise<T> {
    const c = await this.pool.connect();
    try {
      await c.query('BEGIN');
      const r = await fn(c);
      await c.query('COMMIT');
      return r;
    } catch (e) {
      await c.query('ROLLBACK');
      throw e;
    } finally {
      c.release();
    }
  }

  private async sync(fn: () => Promise<void>): Promise<boolean> {
    try {
      await fn();
      return true;
    } catch (e) {
      this.onVoiceError(e);
      return false;
    }
  }

  private async roleOf(q: pg.Pool | pg.PoolClient, roomId: string, uid: string): Promise<Role> {
    const r = await q.query('SELECT role FROM room_roles WHERE room_id = $1 AND uid = $2', [roomId, uid]);
    return (r.rows[0]?.role as Role | undefined) ?? 'member';
  }

  private async openRoom(q: pg.Pool | pg.PoolClient, roomId: string, lock = false) {
    const r = await q.query(
      `SELECT id, owner_uid, max_seats, (password_hash IS NOT NULL) AS is_private FROM rooms WHERE id = $1 AND status = 'open' ${lock ? 'FOR UPDATE' : ''}`,
      [roomId],
    );
    if (!r.rows[0]) throw notFound('room_not_found');
    return r.rows[0] as { id: string; owner_uid: string; max_seats: number; is_private: boolean };
  }

  private async assertNotBanned(q: pg.Pool | pg.PoolClient, roomId: string, uid: string) {
    const r = await q.query('SELECT 1 FROM room_bans WHERE room_id = $1 AND uid = $2', [roomId, uid]);
    if (r.rowCount) throw forbidden('banned_from_room');
  }

  private async hasAccess(q: pg.Pool | pg.PoolClient, roomId: string, uid: string): Promise<boolean> {
    if ((await this.roleOf(q, roomId, uid)) === 'owner') return true;
    const r = await q.query('SELECT 1 FROM room_access WHERE room_id = $1 AND uid = $2', [roomId, uid]);
    return (r.rowCount ?? 0) > 0;
  }

  /** الغرف الخاصة لا يطّلع عليها إلا من دخلها بكلمة السر (أو مالكها). */
  private async assertPrivateAccess(q: pg.Pool | pg.PoolClient, room: { id: string; is_private: boolean }, uid: string) {
    if (room.is_private && !(await this.hasAccess(q, room.id, uid))) throw forbidden('password_required');
  }

  private async requireStaff(q: pg.Pool | pg.PoolClient, roomId: string, uid: string): Promise<Role> {
    const role = await this.roleOf(q, roomId, uid);
    if (role === 'member') throw forbidden('staff_only');
    return role;
  }

  /** المنفِّذ يجب أن تكون رتبته أعلى من رتبة الهدف، ولا يستهدف نفسه. */
  private async assertCanActOn(q: pg.Pool | pg.PoolClient, roomId: string, actor: string, target: string) {
    if (actor === target) throw badRequest('cannot_target_self');
    const a = await this.requireStaff(q, roomId, actor);
    const t = await this.roleOf(q, roomId, target);
    if (RANK[a] <= RANK[t]) throw forbidden('insufficient_role');
  }

  // ---------- الغرف ----------

  async createRoom(
    uid: string,
    title: string,
    topic: string | null,
    country: string | null = null,
    password: string | null = null,
    maxSeats = 9,
  ): Promise<RoomSummary> {
    const cleanTitle = title.trim();
    if (cleanTitle.length < 3 || cleanTitle.length > 60) throw badRequest('invalid_title');
    const cleanTopic = topic?.trim() || null;
    if (cleanTopic && cleanTopic.length > 30) throw badRequest('invalid_topic');
    const cleanCountry = country ? country.trim().toUpperCase() : null;
    if (cleanCountry && !isSupportedCountry(cleanCountry)) throw badRequest('invalid_country');
    if (password !== null && !isValidRoomPassword(password)) throw badRequest('invalid_password');
    const passwordHash = password === null ? null : hashRoomPassword(password);

    try {
      return await this.tx(async (c) => {
        const r = await c.query(
          `INSERT INTO rooms(owner_uid, title, topic, max_seats, country, password_hash) VALUES ($1,$2,$3,$4,$5,$6)
           RETURNING id, created_at`,
          [uid, cleanTitle, cleanTopic, maxSeats, cleanCountry, passwordHash],
        );
        const id = r.rows[0].id as string;
        await c.query(`INSERT INTO room_roles(room_id, uid, role) VALUES ($1,$2,'owner')`, [id, uid]);
        await c.query(`INSERT INTO room_seats(room_id, seat_no, uid) VALUES ($1,0,$2)`, [id, uid]);
        return {
          id,
          title: cleanTitle,
          topic: cleanTopic,
          ownerUid: uid,
          seatedCount: 1,
          maxSeats,
          country: cleanCountry,
          isPrivate: passwordHash !== null,
          createdAt: (r.rows[0].created_at as Date).toISOString(),
        };
      });
    } catch (e) {
      if ((e as { code?: string }).code === '23505') throw conflict('active_room_exists');
      throw e;
    }
  }

  /** followedBy: يقتصر على غرف من يتابعهم هذا المستخدم. */
  async listOpenRooms(limit = 50, country: string | null = null, followedBy: string | null = null): Promise<RoomSummary[]> {
    const cc = country ? country.trim().toUpperCase() : null;
    if (cc && !isSupportedCountry(cc)) throw badRequest('invalid_country');
    const r = await this.pool.query(
      `SELECT r.id, r.title, r.topic, r.owner_uid, r.max_seats, r.country, r.created_at, (r.password_hash IS NOT NULL) AS is_private,
              (SELECT count(*) FROM room_seats s WHERE s.room_id = r.id)::int AS seated
         FROM rooms r WHERE r.status = 'open' AND ($2::text IS NULL OR r.country = $2)
          AND ($3::text IS NULL OR EXISTS (SELECT 1 FROM follows f WHERE f.follower_uid = $3 AND f.followee_uid = r.owner_uid))
         ORDER BY r.created_at DESC LIMIT $1`,
      [Math.min(Math.max(limit, 1), 100), cc, followedBy],
    );
    return r.rows.map((x) => ({
      id: x.id,
      title: x.title,
      topic: x.topic,
      ownerUid: x.owner_uid,
      seatedCount: x.seated,
      maxSeats: x.max_seats,
      country: x.country,
      isPrivate: x.is_private as boolean,
      createdAt: (x.created_at as Date).toISOString(),
    }));
  }

  /** حالة الغرفة للعرض: المقاعد وأدوار أصحابها، مع دور المستدعي. لا تكشف أي بيانات خاصة. */
  async getRoomState(uid: string, roomId: string) {
    const room = await this.pool.query(
      `SELECT id, title, topic, owner_uid, max_seats, created_at, (password_hash IS NOT NULL) AS is_private
         FROM rooms WHERE id = $1 AND status = 'open'`,
      [roomId],
    );
    if (!room.rows[0]) throw notFound('room_not_found');
    await this.assertNotBanned(this.pool, roomId, uid);
    await this.assertPrivateAccess(this.pool, { id: roomId, is_private: room.rows[0].is_private as boolean }, uid);
    const seats = await this.pool.query(
      `SELECT s.seat_no, s.uid, COALESCE(r.role, 'member') AS role
         FROM room_seats s LEFT JOIN room_roles r ON r.room_id = s.room_id AND r.uid = s.uid
        WHERE s.room_id = $1 ORDER BY s.seat_no`,
      [roomId],
    );
    // قيمة الهدايا المستلمة داخل هذه الغرفة لكل مستلم (من سجل الهدايا الفعلي)
    const pts = await this.pool.query(
      `SELECT recipient_uid, SUM(price_coins)::bigint AS pts FROM room_gifts WHERE room_id = $1 GROUP BY recipient_uid`,
      [roomId],
    );
    const points = new Map<string, number>(pts.rows.map((x) => [x.recipient_uid as string, Number(x.pts)]));
    const fr = await this.pool.query(
      `SELECT uid, frame_id FROM user_equipped_frame WHERE uid = ANY($1::text[])`,
      [[...new Set<string>([room.rows[0].owner_uid as string, ...seats.rows.map((x) => x.uid as string)])]],
    );
    const frames = new Map<string, string>(fr.rows.map((x) => [x.uid as string, x.frame_id as string]));
    const giftTotal = [...points.values()].reduce((a, b) => a + b, 0);
    const myRole = await this.roleOf(this.pool, roomId, uid);
    const mine = seats.rows.find((x) => x.uid === uid);
    return {
      id: room.rows[0].id as string,
      title: room.rows[0].title as string,
      topic: (room.rows[0].topic as string | null) ?? null,
      ownerUid: room.rows[0].owner_uid as string,
      maxSeats: room.rows[0].max_seats as number,
      myRole,
      mySeat: (mine?.seat_no as number | undefined) ?? null,
      createdAt: (room.rows[0].created_at as Date).toISOString(),
      giftTotal,
      seats: seats.rows.map((x) => ({
        seat: x.seat_no as number, uid: x.uid as string, role: x.role as Role, points: points.get(x.uid as string) ?? 0,
        frame: frames.get(x.uid as string) ?? null,
      })),
      ownerFrame: frames.get(room.rows[0].owner_uid as string) ?? null,
    };
  }

  async closeRoom(actor: string, roomId: string): Promise<ActionResult> {
    await this.tx(async (c) => {
      await this.openRoom(c, roomId, true);
      if ((await this.roleOf(c, roomId, actor)) !== 'owner') throw forbidden('owner_only');
      await c.query(`UPDATE rooms SET status = 'closed', closed_at = now() WHERE id = $1`, [roomId]);
      await c.query(`UPDATE seat_requests SET status = 'rejected', decided_at = now() WHERE room_id = $1 AND status = 'pending'`, [roomId]);
      await c.query('DELETE FROM room_seats WHERE room_id = $1', [roomId]);
    });
    return { ok: true, voiceSynced: await this.sync(() => this.voice.closeRoom(roomId)) };
  }

  // ---------- الانضمام ----------

  async join(uid: string, roomId: string, password: string | null = null) {
    const room = await this.openRoom(this.pool, roomId);
    await this.assertNotBanned(this.pool, roomId, uid);
    const role = await this.roleOf(this.pool, roomId, uid);
    if (room.is_private && role !== 'owner') await this.checkPassword(roomId, uid, password);
    await this.pool.query(
      'INSERT INTO room_access(room_id, uid) VALUES ($1,$2) ON CONFLICT DO NOTHING',
      [roomId, uid],
    );
    const seat = await this.pool.query('SELECT seat_no FROM room_seats WHERE room_id = $1 AND uid = $2', [roomId, uid]);
    const seatNo: number | null = seat.rows[0]?.seat_no ?? null;
    const token = await this.voice.issueToken({ roomId, uid, canPublish: seatNo !== null });
    return { token, url: this.voice.publicUrl, role, seat: seatNo };
  }

  /** يتحقق من كلمة السر مع حدّ للمحاولات: 5 فاشلة/15 دقيقة لكل مستخدم، و50 فاشلة/ساعة لكل غرفة. */
  private async checkPassword(roomId: string, uid: string, password: string | null) {
    const lim = await this.pool.query(
      `SELECT count(*) FILTER (WHERE uid = $2 AND at > now() - interval '15 minutes')::int AS by_user,
              count(*) FILTER (WHERE at > now() - interval '1 hour')::int AS by_room
         FROM room_password_failures WHERE room_id = $1`,
      [roomId, uid],
    );
    if (lim.rows[0].by_user >= 5 || lim.rows[0].by_room >= 50) throw tooManyRequests('too_many_attempts');

    const row = await this.pool.query('SELECT password_hash FROM rooms WHERE id = $1', [roomId]);
    const ok = password !== null && isValidRoomPassword(password) && verifyRoomPassword(password, row.rows[0].password_hash as string);
    if (!ok) {
      await this.pool.query('INSERT INTO room_password_failures(room_id, uid) VALUES ($1,$2)', [roomId, uid]);
      throw forbidden('wrong_password');
    }
    await this.pool.query('DELETE FROM room_password_failures WHERE room_id = $1 AND uid = $2', [roomId, uid]);
  }

  // ---------- الدردشة ----------

  async postMessage(uid: string, roomId: string, body: string): Promise<ChatMessage> {
    const text = body.trim();
    if (text.length < 1 || text.length > 300) throw badRequest('invalid_message');
    await this.openRoom(this.pool, roomId);
    await this.assertNotBanned(this.pool, roomId, uid);
    if (!(await this.hasAccess(this.pool, roomId, uid))) throw forbidden('join_required');
    const recent = await this.pool.query(
      `SELECT count(*)::int AS n FROM room_messages
        WHERE room_id = $1 AND uid = $2 AND kind = 'text' AND created_at > now() - interval '10 seconds'`,
      [roomId, uid],
    );
    if (recent.rows[0].n >= 5) throw tooManyRequests('rate_limited');
    const r = await this.pool.query(
      'INSERT INTO room_messages(room_id, uid, body) VALUES ($1,$2,$3) RETURNING id, created_at',
      [roomId, uid, text],
    );
    return { id: Number(r.rows[0].id), uid, body: text, kind: 'text', meta: {}, createdAt: (r.rows[0].created_at as Date).toISOString() };
  }

  /** after=null: آخر 50 رسالة. after=n: ما بعد المعرّف n بترتيب تصاعدي (حتى 100). */
  async listMessages(uid: string, roomId: string, after: number | null): Promise<ChatMessage[]> {
    await this.openRoom(this.pool, roomId);
    await this.assertNotBanned(this.pool, roomId, uid);
    if (!(await this.hasAccess(this.pool, roomId, uid))) throw forbidden('join_required');
    const r = after === null
      ? await this.pool.query(
          `SELECT id, uid, body, kind, meta, created_at FROM (
             SELECT id, uid, body, kind, meta, created_at FROM room_messages WHERE room_id = $1 ORDER BY id DESC LIMIT 50
           ) t ORDER BY id`,
          [roomId],
        )
      : await this.pool.query(
          `SELECT id, uid, body, kind, meta, created_at FROM room_messages WHERE room_id = $1 AND id > $2 ORDER BY id LIMIT 100`,
          [roomId, after],
        );
    return r.rows.map((x) => ({
      id: Number(x.id), uid: x.uid as string, body: x.body as string,
      kind: x.kind as 'text' | 'gift', meta: x.meta as Record<string, unknown>, createdAt: (x.created_at as Date).toISOString(),
    }));
  }

  // ---------- المقاعد ----------

  async requestSeat(uid: string, roomId: string): Promise<SeatRequest> {
    const room = await this.openRoom(this.pool, roomId);
    await this.assertNotBanned(this.pool, roomId, uid);
    await this.assertPrivateAccess(this.pool, room, uid);
    const seated = await this.pool.query('SELECT 1 FROM room_seats WHERE room_id = $1 AND uid = $2', [roomId, uid]);
    if (seated.rowCount) throw conflict('already_seated');

    // idempotent: طلب معلّق واحد لكل مستخدم
    const r = await this.pool.query(
      `INSERT INTO seat_requests(room_id, uid) VALUES ($1,$2)
       ON CONFLICT (room_id, uid) WHERE status = 'pending' DO UPDATE SET uid = EXCLUDED.uid
       RETURNING id, room_id, uid, status, created_at`,
      [roomId, uid],
    );
    return this.mapReq(r.rows[0]);
  }

  async listSeatRequests(actor: string, roomId: string): Promise<SeatRequest[]> {
    await this.openRoom(this.pool, roomId);
    await this.requireStaff(this.pool, roomId, actor);
    const r = await this.pool.query(
      `SELECT id, room_id, uid, status, created_at FROM seat_requests
        WHERE room_id = $1 AND status = 'pending' ORDER BY created_at`,
      [roomId],
    );
    return r.rows.map((x) => this.mapReq(x));
  }

  async decideSeatRequest(actor: string, roomId: string, requestId: string, approve: boolean): Promise<ActionResult & { seat?: number }> {
    const out = await this.tx(async (c) => {
      // قفل الغرفة يسلسل الموافقات المتزامنة فلا يُمنح مقعدان لنفس الرقم
      const room = await this.openRoom(c, roomId, true);
      await this.requireStaff(c, roomId, actor);

      const rq = await c.query(
        `SELECT id, uid FROM seat_requests WHERE id = $1 AND room_id = $2 AND status = 'pending' FOR UPDATE`,
        [requestId, roomId],
      );
      if (!rq.rows[0]) throw notFound('request_not_found');
      const targetUid = rq.rows[0].uid as string;

      if (!approve) {
        await c.query(
          `UPDATE seat_requests SET status='rejected', decided_by=$2, decided_at=now() WHERE id=$1`,
          [requestId, actor],
        );
        return { seat: undefined as number | undefined, targetUid };
      }

      await this.assertNotBanned(c, roomId, targetUid);
      const free = await c.query(
        `SELECT s AS seat_no FROM generate_series(0, $2::int - 1) s
          WHERE s NOT IN (SELECT seat_no FROM room_seats WHERE room_id = $1)
          ORDER BY s LIMIT 1`,
        [roomId, room.max_seats],
      );
      if (!free.rows[0]) throw conflict('no_free_seat');
      const seatNo = free.rows[0].seat_no as number;
      await c.query('INSERT INTO room_seats(room_id, seat_no, uid) VALUES ($1,$2,$3)', [roomId, seatNo, targetUid]);
      await c.query(
        `UPDATE seat_requests SET status='approved', decided_by=$2, decided_at=now() WHERE id=$1`,
        [requestId, actor],
      );
      return { seat: seatNo, targetUid };
    });

    const voiceSynced = out.seat === undefined ? true : await this.sync(() => this.voice.setCanPublish(roomId, out.targetUid, true));
    return { ok: true, voiceSynced, seat: out.seat };
  }

  async leaveSeat(uid: string, roomId: string): Promise<ActionResult> {
    await this.openRoom(this.pool, roomId);
    // المالك لا يترك مقعده؛ يغلق الغرفة بدلاً من ذلك
    if ((await this.roleOf(this.pool, roomId, uid)) === 'owner') throw conflict('owner_cannot_leave_seat');
    const r = await this.pool.query('DELETE FROM room_seats WHERE room_id = $1 AND uid = $2', [roomId, uid]);
    if (!r.rowCount) throw conflict('not_seated');
    return { ok: true, voiceSynced: await this.sync(() => this.voice.setCanPublish(roomId, uid, false)) };
  }

  // ---------- الإشراف ----------

  async mute(actor: string, roomId: string, target: string): Promise<ActionResult> {
    await this.openRoom(this.pool, roomId);
    await this.assertCanActOn(this.pool, roomId, actor, target);
    return { ok: true, voiceSynced: await this.sync(() => this.voice.muteParticipant(roomId, target)) };
  }

  async kick(actor: string, roomId: string, target: string, ban: boolean): Promise<ActionResult> {
    await this.tx(async (c) => {
      await this.openRoom(c, roomId, true);
      await this.assertCanActOn(c, roomId, actor, target);
      await c.query('DELETE FROM room_seats WHERE room_id = $1 AND uid = $2', [roomId, target]);
      await c.query(`UPDATE seat_requests SET status='rejected', decided_at=now() WHERE room_id=$1 AND uid=$2 AND status='pending'`, [roomId, target]);
      if (ban) {
        await c.query(`DELETE FROM room_roles WHERE room_id = $1 AND uid = $2 AND role = 'moderator'`, [roomId, target]);
        await c.query(
          'INSERT INTO room_bans(room_id, uid, banned_by) VALUES ($1,$2,$3) ON CONFLICT DO NOTHING',
          [roomId, target, actor],
        );
      }
    });
    return { ok: true, voiceSynced: await this.sync(() => this.voice.removeParticipant(roomId, target)) };
  }

  async setModerator(actor: string, roomId: string, target: string, makeMod: boolean): Promise<void> {
    await this.tx(async (c) => {
      await this.openRoom(c, roomId, true);
      if ((await this.roleOf(c, roomId, actor)) !== 'owner') throw forbidden('owner_only');
      if (target === actor) throw badRequest('cannot_target_self');
      if (makeMod) {
        await this.assertNotBanned(c, roomId, target);
        await c.query(
          `INSERT INTO room_roles(room_id, uid, role) VALUES ($1,$2,'moderator') ON CONFLICT (room_id, uid) DO NOTHING`,
          [roomId, target],
        );
      } else {
        await c.query(`DELETE FROM room_roles WHERE room_id=$1 AND uid=$2 AND role='moderator'`, [roomId, target]);
      }
    });
  }

  private mapReq(x: { id: string; room_id: string; uid: string; status: string; created_at: Date }): SeatRequest {
    return { id: x.id, roomId: x.room_id, uid: x.uid, status: x.status, createdAt: x.created_at.toISOString() };
  }
}
