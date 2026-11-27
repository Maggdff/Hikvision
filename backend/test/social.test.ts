import { describe, it, expect, beforeAll, beforeEach, afterAll } from 'vitest';
import pg from 'pg';
import { buildApp } from '../src/app.js';
import { migrate } from '../src/db/migrate.js';
import type { TokenVerifier } from '../src/auth.js';
import type { VoiceProvider } from '../src/voice.js';

const url = process.env.TEST_DATABASE_URL;
const d = url ? describe : describe.skip;

class FakeVerifier implements TokenVerifier {
  async verify(t: string) { return { uid: t }; }
}
class FakeVoice implements VoiceProvider {
  publicUrl = 'wss://fake';
  async issueToken(p: { uid: string }) { return p.uid; }
  async setCanPublish() {}
  async muteParticipant() {}
  async removeParticipant() {}
  async closeRoom() {}
}

d('social: follows, followed-rooms feed, banners (Postgres)', () => {
  let pool: pg.Pool;
  let app: ReturnType<typeof buildApp>;

  beforeAll(async () => {
    const admin = new pg.Client({ connectionString: url });
    await admin.connect();
    await admin.query('DROP SCHEMA public CASCADE; CREATE SCHEMA public;');
    await admin.end();
    await migrate(url!);
    pool = new pg.Pool({ connectionString: url, max: 10 });
  });
  afterAll(async () => { await app?.close(); await pool?.end(); });
  beforeEach(async () => {
    await pool.query('TRUNCATE rooms, follows, banners CASCADE');
    app = buildApp({ pool, verifier: new FakeVerifier(), voice: new FakeVoice() });
  });

  const call = (uid: string | null, method: 'GET' | 'POST' | 'PUT' | 'DELETE', path: string, body?: object) =>
    app.inject({ method, url: `/v1${path}`, headers: uid ? { authorization: `Bearer ${uid}` } : {}, payload: body });

  it('follow is idempotent and counts are real', async () => {
    expect((await call('uid-a', 'PUT', '/follows/uid-b')).statusCode).toBe(204);
    expect((await call('uid-a', 'PUT', '/follows/uid-b')).statusCode).toBe(204);
    expect((await call('uid-c', 'PUT', '/follows/uid-b')).statusCode).toBe(204);
    const b = (await call('uid-a', 'GET', '/users/uid-b/social')).json();
    expect(b).toEqual({ followers: 2, followingCount: 0, iFollow: true });
    const a = (await call('uid-b', 'GET', '/users/uid-a/social')).json();
    expect(a).toEqual({ followers: 0, followingCount: 1, iFollow: false });
  });

  it('cannot follow yourself; unfollow removes', async () => {
    expect((await call('uid-a', 'PUT', '/follows/uid-a')).statusCode).toBe(400);
    await call('uid-a', 'PUT', '/follows/uid-b');
    expect((await call('uid-a', 'DELETE', '/follows/uid-b')).statusCode).toBe(204);
    expect((await call('uid-a', 'GET', '/users/uid-b/social')).json().iFollow).toBe(false);
  });

  it('requires sign-in and a valid uid', async () => {
    expect((await call(null, 'PUT', '/follows/uid-b')).statusCode).toBe(401);
    expect((await call('uid-a', 'PUT', '/follows/bad%20uid')).statusCode).toBe(400);
  });

  it('the following feed lists only rooms owned by people I follow', async () => {
    const mine = (await call('uid-owner1', 'POST', '/rooms', { title: 'غرفة المتابَع' })).json().id;
    await call('uid-owner2', 'POST', '/rooms', { title: 'غرفة أخرى' });
    await call('uid-fan', 'PUT', '/follows/uid-owner1');
    const feed = (await call('uid-fan', 'GET', '/rooms?scope=following')).json().rooms;
    expect(feed.map((r: { id: string }) => r.id)).toEqual([mine]);
    const all = (await call('uid-fan', 'GET', '/rooms')).json().rooms;
    expect(all).toHaveLength(2);
    // من لا يتابع أحداً لا يرى شيئاً
    expect((await call('uid-nobody', 'GET', '/rooms?scope=following')).json().rooms).toEqual([]);
  });

  it('banners: empty by default, only active ones inside their window are listed', async () => {
    expect((await call('uid-a', 'GET', '/banners')).json().banners).toEqual([]);
    await pool.query(`INSERT INTO banners(title, subtitle, sort_order) VALUES ('فعالة', 'تفاصيل', 1)`);
    await pool.query(`INSERT INTO banners(title, active) VALUES ('معطلة', false)`);
    await pool.query(`INSERT INTO banners(title, ends_at) VALUES ('منتهية', now() - interval '1 day')`);
    await pool.query(`INSERT INTO banners(title, starts_at) VALUES ('لم تبدأ', now() + interval '1 day')`);
    const b = (await call('uid-a', 'GET', '/banners')).json().banners;
    expect(b).toEqual([{ id: expect.any(Number), title: 'فعالة', subtitle: 'تفاصيل' }]);
  });
});
