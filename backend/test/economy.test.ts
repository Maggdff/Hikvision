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
  publicUrl = 'wss://fake';
  async issueToken(p: { uid: string }) { return p.uid; }
  async setCanPublish() {}
  async muteParticipant() {}
  async removeParticipant() {}
  async closeRoom() {}
}

d('economy: wallet, gifts, ledger (Postgres)', () => {
  let pool: pg.Pool;
  let app: ReturnType<typeof buildApp>;
  // الدفتر لا يُمحى (append-only)، فكل اختبار يستخدم مستخدماً جديداً كي لا تتراكم أرصدته
  let fan = '';
  let userSeq = 0;

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
    fan = `uid-fan${++userSeq}`;
    app = buildApp({ pool, verifier: new FakeVerifier(), voice: new FakeVoice(), adminUids: ['uid-admin'] });
  });

  const call = (uid: string | null, method: 'GET' | 'POST', path: string, body?: object, key?: string) =>
    app.inject({
      method, url: `/v1${path}`,
      headers: { ...(uid ? { authorization: `Bearer ${uid}` } : {}), ...(key ? { 'idempotency-key': key } : {}) },
      payload: body,
    });

  let n = 0;
  const key = () => `key-${Date.now()}-${n++}-abcdefgh`;

  async function grant(uid: string, amount: number) {
    const r = await call('uid-admin', 'POST', '/admin/grants', { uid, amount, reason: 'اختبار منحة' }, key());
    expect(r.statusCode).toBe(200);
    return r.json().balance as number;
  }

  /** غرفة مالكها uid-owner (على المنصة)، وuid-fan دخلها. */
  async function room() {
    const r = await call('uid-owner', 'POST', '/rooms', { title: 'غرفة الهدايا' });
    const id = r.json().id as string;
    await call(fan, 'POST', `/rooms/${id}/join`);
    return id;
  }

  const send = (uid: string, roomId: string, giftId: string, to = 'uid-owner', k = key()) =>
    call(uid, 'POST', `/rooms/${roomId}/gifts`, { giftId, recipientUid: to }, k);

  async function sums() {
    const r = await pool.query(
      `SELECT COALESCE(SUM(amount) FILTER (WHERE direction='debit'),0) AS d,
              COALESCE(SUM(amount) FILTER (WHERE direction='credit'),0) AS c FROM ledger_entries`);
    return { d: Number(r.rows[0].d), c: Number(r.rows[0].c) };
  }

  it('lists the seeded active catalog ordered by sort order', async () => {
    const r = await call(fan, 'GET', '/gifts');
    const gifts = r.json().gifts as { id: string; priceCoins: number }[];
    expect(gifts.length).toBeGreaterThanOrEqual(8);
    expect(gifts[0].id).toBe('rose');
    expect(gifts.every((g) => g.priceCoins > 0)).toBe(true);
  });

  it('a new wallet is empty', async () => {
    expect((await call(fan, 'GET', '/wallet')).json().balance).toBe(0);
  });

  it('only admins can grant coins', async () => {
    const r = await call(fan, 'POST', '/admin/grants', { uid: fan, amount: 100, reason: 'محاولة' }, key());
    expect(r.statusCode).toBe(403);
    expect(r.json().error).toBe('admin_only');
    expect((await call(fan, 'GET', '/wallet')).json().balance).toBe(0);
  });

  it('grants are validated', async () => {
    for (const body of [
      { uid: fan, amount: 0, reason: 'سبب صحيح' },
      { uid: fan, amount: -5, reason: 'سبب صحيح' },
      { uid: fan, amount: 1.5, reason: 'سبب صحيح' },
      { uid: fan, amount: 2_000_000, reason: 'سبب صحيح' },
      { uid: fan, amount: 10, reason: 'ab' },
    ]) {
      const r = await call('uid-admin', 'POST', '/admin/grants', body, key());
      expect(r.statusCode, JSON.stringify(body)).toBe(400);
    }
    const noKey = await call('uid-admin', 'POST', '/admin/grants', { uid: fan, amount: 10, reason: 'سبب صحيح' });
    expect(noKey.statusCode).toBe(400);
  });

  it('a grant is idempotent: same key credits once', async () => {
    const k = key();
    const body = { uid: fan, amount: 500, reason: 'منحة ترحيب' };
    const a = await call('uid-admin', 'POST', '/admin/grants', body, k);
    const b = await call('uid-admin', 'POST', '/admin/grants', body, k);
    expect(a.json()).toMatchObject({ balance: 500, replayed: false });
    expect(b.json()).toMatchObject({ balance: 500, replayed: true });
    expect((await call(fan, 'GET', '/wallet')).json().balance).toBe(500);
  });

  it('sending a gift debits the wallet, posts a gift message, and keeps the ledger balanced', async () => {
    await grant(fan, 1000);
    const id = await room();
    const r = await send(fan, id, 'crown');
    expect(r.statusCode).toBe(200);
    expect(r.json()).toMatchObject({ ok: true, balance: 500, replayed: false });
    expect((await call(fan, 'GET', '/wallet')).json().balance).toBe(500);

    const msgs = (await call(fan, 'GET', `/rooms/${id}/messages`)).json().messages;
    const gift = msgs.find((m: { kind: string }) => m.kind === 'gift');
    expect(gift.uid).toBe(fan);
    expect(gift.meta).toMatchObject({ giftId: 'crown', recipientUid: 'uid-owner', priceCoins: 500 });

    const s = await sums();
    expect(s.d).toBe(s.c);

    // حالة الغرفة تعرض قيمة الهدايا المستلمة لكل جالس ومجموع الغرفة
    const st = (await call(fan, 'GET', `/rooms/${id}`)).json();
    expect(st.giftTotal).toBe(500);
    expect(st.seats.find((x: { uid: string }) => x.uid === 'uid-owner').points).toBe(500);
    expect(typeof st.createdAt).toBe('string');
  });

  it('the same idempotency key never charges twice', async () => {
    await grant(fan, 1000);
    const id = await room();
    const k = key();
    const a = await send(fan, id, 'star', 'uid-owner', k);
    const b = await send(fan, id, 'star', 'uid-owner', k);
    expect(a.json().replayed).toBe(false);
    expect(b.json()).toMatchObject({ replayed: true, balance: 800 });
    expect(b.json().giftMessageId).toBe(a.json().giftMessageId);
    expect((await call(fan, 'GET', '/wallet')).json().balance).toBe(800);
    const rows = await pool.query('SELECT count(*)::int AS n FROM room_gifts');
    expect(rows.rows[0].n).toBe(1);
  });

  it('refuses when funds are insufficient and changes nothing', async () => {
    await grant(fan, 40);
    const id = await room();
    const r = await send(fan, id, 'heart'); // 50
    expect(r.statusCode).toBe(402);
    expect(r.json().error).toBe('insufficient_funds');
    expect((await call(fan, 'GET', '/wallet')).json().balance).toBe(40);
    expect((await pool.query('SELECT count(*)::int AS n FROM room_gifts')).rows[0].n).toBe(0);
  });

  it('parallel gifts can never overdraw: 250 coins buys exactly 2 of the 100-coin gift', async () => {
    await grant(fan, 250);
    const id = await room();
    const results = await Promise.all(Array.from({ length: 10 }, () => send(fan, id, 'coffee')));
    const ok = results.filter((r) => r.statusCode === 200).length;
    const refused = results.filter((r) => r.statusCode === 402).length;
    expect(ok).toBe(2);
    expect(refused).toBe(8);
    expect((await call(fan, 'GET', '/wallet')).json().balance).toBe(50);
    const s = await sums();
    expect(s.d).toBe(s.c);
  });

  it('validates recipient, room and gift', async () => {
    await grant(fan, 1000);
    const id = await room();
    expect((await send(fan, id, 'rose', fan)).json().error).toBe('cannot_gift_self');
    const notOnStage = await send(fan, id, 'rose', 'uid-nobody');
    expect(notOnStage.statusCode).toBe(409);
    expect(notOnStage.json().error).toBe('recipient_not_on_stage');
    expect((await send(fan, id, 'unicorn')).statusCode).toBe(404);
    await pool.query(`UPDATE gifts SET active = false WHERE id = 'rose'`);
    expect((await send(fan, id, 'rose')).json().error).toBe('gift_not_found');
    await pool.query(`UPDATE gifts SET active = true WHERE id = 'rose'`);
    const stranger = await send('uid-stranger', id, 'rose');
    expect(stranger.statusCode).toBe(403); // لم يدخل الغرفة
    expect(await call(fan, 'POST', `/rooms/${id}/gifts`, { giftId: 'rose', recipientUid: 'uid-owner' })).toHaveProperty('statusCode', 400);
    expect((await call(fan, 'GET', '/wallet')).json().balance).toBe(1000);
  });

  it('banned users cannot send gifts', async () => {
    await grant(fan, 1000);
    const id = await room();
    await pool.query(`INSERT INTO room_bans(room_id, uid, banned_by) VALUES ($1,$2,'uid-owner')`, [id, fan]);
    expect((await send(fan, id, 'rose')).json().error).toBe('banned_from_room');
  });

  it('a gift price change does not rewrite history', async () => {
    await grant(fan, 1000);
    const id = await room();
    await send(fan, id, 'rose');
    await pool.query(`UPDATE gifts SET price_coins = 99 WHERE id = 'rose'`);
    const row = await pool.query(`SELECT price_coins FROM room_gifts LIMIT 1`);
    expect(row.rows[0].price_coins).toBe(10);
    await pool.query(`UPDATE gifts SET price_coins = 10 WHERE id = 'rose'`);
  });

  it('gift messages do not count against the chat rate limit', async () => {
    await grant(fan, 1000);
    const id = await room();
    for (let i = 0; i < 6; i++) expect((await send(fan, id, 'rose')).statusCode).toBe(200);
    expect((await call(fan, 'POST', `/rooms/${id}/messages`, { body: 'مرحبا' })).statusCode).toBe(201);
  });

  it('levels: a user with no gifts is level 1 with 0 points', async () => {
    const r = await call(fan, 'GET', '/levels');
    expect(r.statusCode).toBe(200);
    expect(r.json()).toEqual({
      wealth: { points: 0, level: 1, nextAt: 50 },
      charm: { points: 0, level: 1, nextAt: 50 },
    });
  });

  it('levels: wealth grows for the sender and charm for the receiver, from real gifts only', async () => {
    await grant(fan, 1000);
    const id = await room();
    await send(fan, id, 'crown'); // 500 نقطة
    const mine = (await call(fan, 'GET', '/levels')).json();
    expect(mine.wealth.points).toBe(500);
    expect(mine.wealth.level).toBe(4); // عتبات البذرة: 50×(n−1)² → المستوى 4 عند 450، 5 عند 800
    expect(mine.wealth.nextAt).toBe(800);
    expect(mine.charm.points).toBe(0);

    const owner = (await call(fan, 'GET', '/users/uid-owner/levels')).json();
    expect(owner.charm.points).toBe(500);
    expect(owner.charm.level).toBe(4);
    expect(owner.wealth.points).toBe(0);
  });

  it('levels: thresholds are data — editing the table changes the level', async () => {
    await grant(fan, 1000);
    const id = await room();
    await send(fan, id, 'crown');
    await pool.query(`UPDATE level_thresholds SET min_points = 100 WHERE kind='wealth' AND level = 2`);
    await pool.query(`UPDATE level_thresholds SET min_points = 400 WHERE kind='wealth' AND level = 3`);
    await pool.query(`UPDATE level_thresholds SET min_points = 450 WHERE kind='wealth' AND level = 4`);
    const w = (await call(fan, 'GET', '/levels')).json().wealth;
    expect(w.level).toBe(4);
    await pool.query(`UPDATE level_thresholds SET min_points = 50 * (level - 1) * (level - 1) WHERE kind='wealth'`);
  });

  it('levels: require a signed-in user and a valid uid', async () => {
    expect((await call(null, 'GET', '/levels')).statusCode).toBe(401);
    expect((await call(fan, 'GET', '/users/bad%20uid/levels')).statusCode).toBe(400);
  });
});
