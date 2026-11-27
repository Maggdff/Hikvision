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

d('store: frames + bugatti gift (Postgres)', () => {
  let pool: pg.Pool;
  let app: ReturnType<typeof buildApp>;
  let user = '';
  let seq = 0;
  let n = 0;
  const key = () => `key-${Date.now()}-${n++}-abcdefgh`;

  beforeAll(async () => {
    const admin = new pg.Client({ connectionString: url });
    await admin.connect();
    await admin.query('DROP SCHEMA public CASCADE; CREATE SCHEMA public;');
    await admin.end();
    await migrate(url!);
    pool = new pg.Pool({ connectionString: url, max: 20 });
  });
  afterAll(async () => { await app?.close(); await pool?.end(); });
  beforeEach(() => {
    user = `uid-buyer${++seq}`;
    app = buildApp({ pool, verifier: new FakeVerifier(), voice: new FakeVoice(), adminUids: ['uid-admin'] });
  });

  const call = (uid: string, method: 'GET' | 'POST' | 'PUT', path: string, body?: object, k?: string) =>
    app.inject({
      method, url: `/v1${path}`,
      headers: { authorization: `Bearer ${uid}`, ...(k ? { 'idempotency-key': k } : {}) },
      payload: body,
    });
  const grant = (uid: string, amount: number) =>
    call('uid-admin', 'POST', '/admin/grants', { uid, amount, reason: 'اختبار منحة' }, key());

  it('catalog has the bugatti gift and the vip6 frame', async () => {
    const g = (await call(user, 'GET', '/gifts')).json().gifts as Array<{ id: string; priceCoins: number; tier: string }>;
    expect(g.find((x) => x.id === 'bugatti')).toMatchObject({ priceCoins: 20000, tier: 'large' });
    const f = (await call(user, 'GET', '/store/frames')).json().frames;
    expect(f).toEqual([expect.objectContaining({ id: 'vip6', owned: false, equipped: false, priceCoins: 5000 })]);
  });

  it('buy needs funds, deducts via ledger, is idempotent, and cannot be bought twice', async () => {
    expect((await call(user, 'POST', '/store/frames/vip6/buy', {}, key())).statusCode).toBe(402);
    await grant(user, 6000);
    const k = key();
    const a = await call(user, 'POST', '/store/frames/vip6/buy', {}, k);
    expect(a.statusCode).toBe(200);
    expect(a.json()).toMatchObject({ balance: 1000, replayed: false });
    const again = (await call(user, 'POST', '/store/frames/vip6/buy', {}, k)).json();
    expect(again).toMatchObject({ balance: 1000, replayed: true });
    expect((await call(user, 'POST', '/store/frames/vip6/buy', {}, key())).statusCode).toBe(409);
    expect((await call(user, 'GET', '/wallet')).json().balance).toBe(1000);
    expect((await call(user, 'POST', '/store/frames/nope/buy', {}, key())).statusCode).toBe(404);
  });

  it('equip only owned frames; frame shows on room state seats and social', async () => {
    expect((await call(user, 'PUT', '/me/frame', { frameId: 'vip6' })).statusCode).toBe(403);
    await grant(user, 5000);
    await call(user, 'POST', '/store/frames/vip6/buy', {}, key());
    expect((await call(user, 'PUT', '/me/frame', { frameId: 'vip6' })).statusCode).toBe(204);
    expect((await call(user, 'GET', '/me')).json()).toMatchObject({ isAdmin: false, frame: 'vip6' });
    expect((await call(user, 'GET', `/users/${user}/social`)).json().frame).toBe('vip6');

    const room = (await call(user, 'POST', '/rooms', { title: 'غرفة الاختبار' })).json();
    await call(user, 'POST', `/rooms/${room.id}/join`, {});
    const st = (await call(user, 'GET', `/rooms/${room.id}`)).json();
    expect(st.ownerFrame).toBe('vip6');
    expect(st.seats[0].frame).toBe('vip6');

    expect((await call(user, 'PUT', '/me/frame', { frameId: null })).statusCode).toBe(204);
    expect((await call(user, 'GET', '/me')).json().frame).toBeNull();
  });

  it('isAdmin is true only for configured admins', async () => {
    expect((await call('uid-admin', 'GET', '/me')).json().isAdmin).toBe(true);
  });
});
