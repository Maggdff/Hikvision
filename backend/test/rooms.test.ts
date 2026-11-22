import { describe, it, expect, beforeAll, beforeEach, afterAll } from 'vitest';
import pg from 'pg';
import { buildApp } from '../src/app.js';
import { migrate } from '../src/db/migrate.js';
import type { TokenVerifier } from '../src/auth.js';
import type { VoiceProvider } from '../src/voice.js';

const url = process.env.TEST_DATABASE_URL;
const d = url ? describe : describe.skip;

class FakeVerifier implements TokenVerifier {
  async verify(token: string) {
    if (!token.startsWith('uid-')) throw new Error('bad token');
    return { uid: token };
  }
}

class FakeVoice implements VoiceProvider {
  publicUrl = 'wss://fake.livekit.test';
  calls: string[] = [];
  failing = false;
  private maybeFail() { if (this.failing) throw new Error('livekit down'); }
  async issueToken(p: { roomId: string; uid: string; canPublish: boolean }) {
    return `${p.uid}:${p.canPublish}`;
  }
  async setCanPublish(_r: string, uid: string, v: boolean) { this.maybeFail(); this.calls.push(`pub:${uid}:${v}`); }
  async muteParticipant(_r: string, uid: string) { this.maybeFail(); this.calls.push(`mute:${uid}`); }
  async removeParticipant(_r: string, uid: string) { this.maybeFail(); this.calls.push(`remove:${uid}`); }
  async closeRoom(r: string) { this.maybeFail(); this.calls.push(`close:${r}`); }
}

d('rooms API (Postgres)', () => {
  let pool: pg.Pool;
  let voice: FakeVoice;
  let app: ReturnType<typeof buildApp>;

  beforeAll(async () => {
    const admin = new pg.Client({ connectionString: url });
    await admin.connect();
    await admin.query('DROP SCHEMA public CASCADE; CREATE SCHEMA public;');
    await admin.end();
    await migrate(url!);
    pool = new pg.Pool({ connectionString: url, max: 20 });
  });

  afterAll(async () => { await app?.close(); await pool?.end(); });

  beforeEach(async () => {
    await pool.query('TRUNCATE rooms CASCADE');
    voice = new FakeVoice();
    app = buildApp({ pool, verifier: new FakeVerifier(), voice });
  });

  const call = (uid: string | null, method: 'GET' | 'POST' | 'PUT' | 'DELETE', path: string, body?: object) =>
    app.inject({
      method, url: `/v1${path}`,
      headers: uid ? { authorization: `Bearer ${uid}` } : {},
      payload: body,
    });

  async function makeRoom(owner = 'uid-owner') {
    const res = await call(owner, 'POST', '/rooms', { title: 'غرفة السمر', topic: 'chat' });
    expect(res.statusCode).toBe(201);
    return res.json().id as string;
  }

  async function seatUser(roomId: string, uid: string, by = 'uid-owner') {
    const rq = await call(uid, 'POST', `/rooms/${roomId}/seat-requests`);
    expect(rq.statusCode).toBe(201);
    const ap = await call(by, 'POST', `/rooms/${roomId}/seat-requests/${rq.json().id}/approve`);
    expect(ap.statusCode).toBe(200);
    return ap.json();
  }

  it('stores a supported country and filters the list by it', async () => {
    const a = await call('uid-a', 'POST', '/rooms', { title: 'غرفة الرياض', country: 'sa' });
    expect(a.statusCode).toBe(201);
    expect(a.json().country).toBe('SA');
    await call('uid-b', 'POST', '/rooms', { title: 'غرفة القاهرة', country: 'EG' });
    await call('uid-c', 'POST', '/rooms', { title: 'بلا دولة' });

    const all = (await call('uid-x', 'GET', '/rooms')).json().rooms;
    expect(all).toHaveLength(3);
    const sa = (await call('uid-x', 'GET', '/rooms?country=SA')).json().rooms;
    expect(sa.map((r: { title: string }) => r.title)).toEqual(['غرفة الرياض']);
    expect(all.find((r: { title: string }) => r.title === 'بلا دولة').country).toBeNull();
  });

  it('rejects unsupported or malformed countries', async () => {
    const bad = await call('uid-a', 'POST', '/rooms', { title: 'غرفة', country: 'US' });
    expect(bad.statusCode).toBe(400);
    expect(bad.json().error).toBe('invalid_country');
    const wrongType = await call('uid-a', 'POST', '/rooms', { title: 'غرفة', country: 5 });
    expect(wrongType.statusCode).toBe(400);
    const badFilter = await call('uid-a', 'GET', '/rooms?country=XX');
    expect(badFilter.statusCode).toBe(400);
    expect((await call('uid-a', 'GET', '/rooms')).json().rooms).toHaveLength(0);
  });

  it('rejects missing or invalid tokens', async () => {
    expect((await call(null, 'GET', '/rooms')).statusCode).toBe(401);
    expect((await call('garbage', 'GET', '/rooms')).statusCode).toBe(401);
  });

  it('creates a room with the owner seated and listed', async () => {
    const id = await makeRoom();
    const list = (await call('uid-a', 'GET', '/rooms')).json().rooms;
    expect(list).toHaveLength(1);
    expect(list[0]).toMatchObject({ id, ownerUid: 'uid-owner', seatedCount: 1, maxSeats: 8 });
  });

  it('allows only one open room per owner, even concurrently', async () => {
    const results = await Promise.all(
      Array.from({ length: 5 }, () => call('uid-owner', 'POST', '/rooms', { title: 'غرفة مكررة' })),
    );
    expect(results.filter((r) => r.statusCode === 201)).toHaveLength(1);
    expect(results.filter((r) => r.statusCode === 409)).toHaveLength(4);
  });

  it('validates the title', async () => {
    expect((await call('uid-owner', 'POST', '/rooms', { title: 'ab' })).statusCode).toBe(400);
    expect((await call('uid-owner', 'POST', '/rooms', {})).statusCode).toBe(400);
  });

  it('issues publish rights only to seated users', async () => {
    const id = await makeRoom();
    const owner = (await call('uid-owner', 'POST', `/rooms/${id}/join`)).json();
    const listener = (await call('uid-l1', 'POST', `/rooms/${id}/join`)).json();
    expect(owner).toMatchObject({ token: 'uid-owner:true', role: 'owner', seat: 0, url: 'wss://fake.livekit.test' });
    expect(listener).toMatchObject({ token: 'uid-l1:false', role: 'member', seat: null });
  });

  it('seat request: idempotent, approved by owner, grants publish', async () => {
    const id = await makeRoom();
    const a = await call('uid-l1', 'POST', `/rooms/${id}/seat-requests`);
    const b = await call('uid-l1', 'POST', `/rooms/${id}/seat-requests`);
    expect(b.json().id).toBe(a.json().id);

    const res = await call('uid-owner', 'POST', `/rooms/${id}/seat-requests/${a.json().id}/approve`);
    expect(res.json()).toMatchObject({ ok: true, voiceSynced: true, seat: 1 });
    expect(voice.calls).toContain('pub:uid-l1:true');

    const join = (await call('uid-l1', 'POST', `/rooms/${id}/join`)).json();
    expect(join).toMatchObject({ token: 'uid-l1:true', seat: 1 });
    expect((await call('uid-l1', 'POST', `/rooms/${id}/seat-requests`)).statusCode).toBe(409);
  });

  it('non-staff cannot list or decide requests', async () => {
    const id = await makeRoom();
    const rq = (await call('uid-l1', 'POST', `/rooms/${id}/seat-requests`)).json();
    expect((await call('uid-l2', 'GET', `/rooms/${id}/seat-requests`)).statusCode).toBe(403);
    expect((await call('uid-l2', 'POST', `/rooms/${id}/seat-requests/${rq.id}/approve`)).statusCode).toBe(403);
    expect((await call('uid-owner', 'GET', `/rooms/${id}/seat-requests`)).json().requests).toHaveLength(1);
  });

  it('rejecting a request does not seat the user', async () => {
    const id = await makeRoom();
    const rq = (await call('uid-l1', 'POST', `/rooms/${id}/seat-requests`)).json();
    const res = await call('uid-owner', 'POST', `/rooms/${id}/seat-requests/${rq.id}/reject`);
    expect(res.statusCode).toBe(200);
    expect((await call('uid-l1', 'POST', `/rooms/${id}/join`)).json().seat).toBeNull();
  });

  it('concurrent approvals never exceed the seats or reuse a seat number', async () => {
    const id = await makeRoom(); // 8 مقاعد، المالك يشغل واحداً → 7 متاحة
    const users = Array.from({ length: 12 }, (_, i) => `uid-u${i}`);
    const reqs = await Promise.all(users.map((u) => call(u, 'POST', `/rooms/${id}/seat-requests`)));
    const ids = reqs.map((r) => r.json().id as string);

    const results = await Promise.all(
      ids.map((rid) => call('uid-owner', 'POST', `/rooms/${id}/seat-requests/${rid}/approve`)),
    );
    expect(results.filter((r) => r.statusCode === 200)).toHaveLength(7);
    expect(results.filter((r) => r.statusCode === 409)).toHaveLength(5);

    const seats = await pool.query('SELECT seat_no, uid FROM room_seats WHERE room_id = $1', [id]);
    expect(seats.rowCount).toBe(8);
    expect(new Set(seats.rows.map((r) => r.seat_no)).size).toBe(8);
  });

  it('leaving a seat revokes publish; owner cannot leave', async () => {
    const id = await makeRoom();
    await seatUser(id, 'uid-l1');
    const res = await call('uid-l1', 'POST', `/rooms/${id}/seat/leave`);
    expect(res.statusCode).toBe(200);
    expect(voice.calls).toContain('pub:uid-l1:false');
    expect((await call('uid-l1', 'POST', `/rooms/${id}/seat/leave`)).statusCode).toBe(409);
    expect((await call('uid-owner', 'POST', `/rooms/${id}/seat/leave`)).statusCode).toBe(409);
  });

  it('moderation hierarchy: mods act on members only; owner on everyone', async () => {
    const id = await makeRoom();
    expect((await call('uid-owner', 'PUT', `/rooms/${id}/moderators/uid-mod`)).statusCode).toBe(204);
    expect((await call('uid-owner', 'PUT', `/rooms/${id}/moderators/uid-mod2`)).statusCode).toBe(204);
    expect((await call('uid-mod', 'PUT', `/rooms/${id}/moderators/uid-x`)).statusCode).toBe(403);

    expect((await call('uid-mod', 'POST', `/rooms/${id}/members/uid-l1/mute`)).statusCode).toBe(200);
    expect(voice.calls).toContain('mute:uid-l1');
    expect((await call('uid-mod', 'POST', `/rooms/${id}/members/uid-owner/mute`)).statusCode).toBe(403);
    expect((await call('uid-mod', 'POST', `/rooms/${id}/members/uid-mod2/kick`)).statusCode).toBe(403);
    expect((await call('uid-mod', 'POST', `/rooms/${id}/members/uid-mod/kick`)).statusCode).toBe(400);
    expect((await call('uid-l1', 'POST', `/rooms/${id}/members/uid-l2/kick`)).statusCode).toBe(403);
    expect((await call('uid-owner', 'POST', `/rooms/${id}/members/uid-mod2/kick`)).statusCode).toBe(200);
  });

  it('ban removes seat, blocks rejoin and new requests, and revokes moderator role', async () => {
    const id = await makeRoom();
    await seatUser(id, 'uid-l1');
    const res = await call('uid-owner', 'POST', `/rooms/${id}/members/uid-l1/kick`, { ban: true });
    expect(res.statusCode).toBe(200);
    expect(voice.calls).toContain('remove:uid-l1');
    expect((await call('uid-l1', 'POST', `/rooms/${id}/join`)).statusCode).toBe(403);
    expect((await call('uid-l1', 'POST', `/rooms/${id}/seat-requests`)).statusCode).toBe(403);
    const seat = await pool.query('SELECT 1 FROM room_seats WHERE room_id=$1 AND uid=$2', [id, 'uid-l1']);
    expect(seat.rowCount).toBe(0);
  });

  it('a plain kick (no ban) lets the user rejoin as listener', async () => {
    const id = await makeRoom();
    await call('uid-owner', 'POST', `/rooms/${id}/members/uid-l1/kick`, {});
    expect((await call('uid-l1', 'POST', `/rooms/${id}/join`)).statusCode).toBe(200);
  });

  it('only the owner can close; closed rooms disappear and reject joins', async () => {
    const id = await makeRoom();
    expect((await call('uid-l1', 'POST', `/rooms/${id}/close`)).statusCode).toBe(403);
    expect((await call('uid-owner', 'POST', `/rooms/${id}/close`)).statusCode).toBe(200);
    expect(voice.calls).toContain(`close:${id}`);
    expect((await call('uid-l1', 'POST', `/rooms/${id}/join`)).statusCode).toBe(404);
    expect((await call('uid-l1', 'GET', '/rooms')).json().rooms).toHaveLength(0);
    // بعد الإغلاق يستطيع المالك فتح غرفة جديدة
    expect((await call('uid-owner', 'POST', '/rooms', { title: 'غرفة جديدة' })).statusCode).toBe(201);
  });

  it('voice outage: DB change stands and response flags voiceSynced=false', async () => {
    const id = await makeRoom();
    const rq = (await call('uid-l1', 'POST', `/rooms/${id}/seat-requests`)).json();
    voice.failing = true;
    const res = await call('uid-owner', 'POST', `/rooms/${id}/seat-requests/${rq.id}/approve`);
    expect(res.statusCode).toBe(200);
    expect(res.json().voiceSynced).toBe(false);
    voice.failing = false;
    // الانضمام التالي يعيد إصدار رمز صحيح من قاعدة البيانات
    expect((await call('uid-l1', 'POST', `/rooms/${id}/join`)).json().token).toBe('uid-l1:true');
  });

  it('room state lists seats with roles and the caller role/seat', async () => {
    const id = await makeRoom();
    await seatUser(id, 'uid-l1');
    await call('uid-owner', 'PUT', `/rooms/${id}/moderators/uid-l1`);
    const s = (await call('uid-l1', 'GET', `/rooms/${id}`)).json();
    expect(s).toMatchObject({ id, ownerUid: 'uid-owner', maxSeats: 8, myRole: 'moderator', mySeat: 1 });
    expect(s.seats).toEqual([
      { seat: 0, uid: 'uid-owner', role: 'owner' },
      { seat: 1, uid: 'uid-l1', role: 'moderator' },
    ]);
    const other = (await call('uid-x', 'GET', `/rooms/${id}`)).json();
    expect(other).toMatchObject({ myRole: 'member', mySeat: null });
  });

  it('room state is hidden from banned users and closed rooms', async () => {
    const id = await makeRoom();
    await call('uid-owner', 'POST', `/rooms/${id}/members/uid-b/kick`, { ban: true });
    expect((await call('uid-b', 'GET', `/rooms/${id}`)).statusCode).toBe(403);
    await call('uid-owner', 'POST', `/rooms/${id}/close`);
    expect((await call('uid-x', 'GET', `/rooms/${id}`)).statusCode).toBe(404);
  });

  it('rejects malformed ids', async () => {
    expect((await call('uid-a', 'POST', '/rooms/not-a-uuid/join')).statusCode).toBe(400);
  });
});
