import { describe, it, expect, beforeAll, afterAll } from 'vitest';
import pg from 'pg';
import { migrate } from '../src/db/migrate.js';

const url = process.env.TEST_DATABASE_URL;
const d = url ? describe : describe.skip;

d('ledger (Postgres)', () => {
  let client: pg.Client;
  let wallet: number;
  let revenue: number;

  beforeAll(async () => {
    const admin = new pg.Client({ connectionString: url });
    await admin.connect();
    await admin.query('DROP SCHEMA public CASCADE; CREATE SCHEMA public;');
    await admin.end();
    await migrate(url!);
    client = new pg.Client({ connectionString: url });
    await client.connect();
    wallet = (await client.query(
      `INSERT INTO ledger_accounts(code,name,kind,currency) VALUES ('W','wallet','wallet_points','USD') RETURNING id`)).rows[0].id;
    revenue = (await client.query(
      `INSERT INTO ledger_accounts(code,name,kind,currency) VALUES ('R','rev','revenue','USD') RETURNING id`)).rows[0].id;
  });

  afterAll(async () => { await client?.end(); });

  async function post(key: string, d: string, c: string) {
    await client.query('BEGIN');
    try {
      const tx = (await client.query(
        `INSERT INTO ledger_transactions(idempotency_key,type) VALUES ($1,'gift_sent') RETURNING id`, [key])).rows[0].id;
      await client.query(
        `INSERT INTO ledger_entries(transaction_id,account_id,direction,amount) VALUES ($1,$2,'debit',$3)`, [tx, wallet, d]);
      await client.query(
        `INSERT INTO ledger_entries(transaction_id,account_id,direction,amount) VALUES ($1,$2,'credit',$3)`, [tx, revenue, c]);
      await client.query('COMMIT');
      return Number(tx);
    } catch (e) {
      await client.query('ROLLBACK');
      throw e;
    }
  }

  it('accepts a balanced transaction', async () => {
    await expect(post('k1', '10.500000', '10.500000')).resolves.toBeGreaterThan(0);
  });

  it('rejects an unbalanced transaction', async () => {
    await expect(post('k2', '10', '9.999999')).rejects.toThrow(/unbalanced/);
  });

  it('rejects a duplicate idempotency key', async () => {
    await expect(post('k1', '1', '1')).rejects.toThrow(/duplicate key/);
  });

  it('blocks UPDATE and DELETE on entries and transactions', async () => {
    await expect(client.query('UPDATE ledger_entries SET amount = 1')).rejects.toThrow(/append-only/);
    await expect(client.query('DELETE FROM ledger_entries')).rejects.toThrow(/append-only/);
    await expect(client.query(`UPDATE ledger_transactions SET type='refund'`)).rejects.toThrow(/append-only/);
  });

  it('stores amounts as exact decimals', async () => {
    const r = await client.query(`SELECT SUM(amount)::text AS s FROM ledger_entries WHERE direction='debit'`);
    expect(r.rows[0].s).toBe('10.500000');
  });
});
