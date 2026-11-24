import pg from 'pg';
import { badRequest, conflict, forbidden, notFound, paymentRequired } from '../errors.js';

export interface Gift {
  id: string;
  nameAr: string;
  nameEn: string;
  priceCoins: number;
  tier: 'small' | 'medium' | 'large';
}

export interface SendGiftResult {
  ok: true;
  giftMessageId: number;
  balance: number;
  replayed: boolean;
}

const MAX_GRANT = 1_000_000;

/**
 * العملات والهدايا فوق الدفتر المحاسبي. الرصيد = مجموع دائن محفظة المستخدم − مجموع مدينها،
 * ولا يُخزَّن في أي عمود قابل للتعديل. كل حركة معاملة متوازنة بمفتاح idempotency.
 */
export class EconomyService {
  constructor(private readonly pool: pg.Pool) {}

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

  /** ينشئ محفظة المستخدم إن لزم ويقفل صفها: يسلسل كل الحركات على نفس المحفظة فلا سحب مزدوج. */
  private async lockWallet(c: pg.PoolClient, uid: string): Promise<number> {
    await c.query(
      `INSERT INTO ledger_accounts(code, name, kind, currency) VALUES ($1, $2, 'wallet_points', 'COIN')
       ON CONFLICT (code) DO NOTHING`,
      [`wallet:${uid}`, `Wallet ${uid}`],
    );
    const r = await c.query('SELECT id FROM ledger_accounts WHERE code = $1 FOR UPDATE', [`wallet:${uid}`]);
    return Number(r.rows[0].id);
  }

  private async platformAccount(c: pg.PoolClient, code: string): Promise<number> {
    const r = await c.query('SELECT id FROM ledger_accounts WHERE code = $1', [code]);
    if (!r.rows[0]) throw new Error(`missing ledger account ${code}`);
    return Number(r.rows[0].id);
  }

  private async balanceOf(q: pg.Pool | pg.PoolClient, accountId: number): Promise<number> {
    const r = await q.query(
      `SELECT COALESCE(SUM(amount) FILTER (WHERE direction = 'credit'), 0)
            - COALESCE(SUM(amount) FILTER (WHERE direction = 'debit'), 0) AS bal
         FROM ledger_entries WHERE account_id = $1`,
      [accountId],
    );
    return Number(r.rows[0].bal);
  }

  async getBalance(uid: string): Promise<number> {
    const r = await this.pool.query('SELECT id FROM ledger_accounts WHERE code = $1', [`wallet:${uid}`]);
    return r.rows[0] ? this.balanceOf(this.pool, Number(r.rows[0].id)) : 0;
  }

  async listGifts(): Promise<Gift[]> {
    const r = await this.pool.query(
      `SELECT id, name_ar, name_en, price_coins, tier FROM gifts WHERE active ORDER BY sort_order, price_coins`,
    );
    return r.rows.map((x) => ({
      id: x.id, nameAr: x.name_ar, nameEn: x.name_en, priceCoins: x.price_coins, tier: x.tier,
    }));
  }

  /** منحة ترويجية يصدرها مشرف النظام. تُسجَّل في الدفتر: مدين حساب الترويج، دائن محفظة المستخدم. */
  async grant(adminUid: string, uid: string, amount: number, reason: string, key: string): Promise<{ balance: number; replayed: boolean }> {
    if (!Number.isInteger(amount) || amount < 1 || amount > MAX_GRANT) throw badRequest('invalid_amount');
    const why = reason.trim();
    if (why.length < 3 || why.length > 200) throw badRequest('invalid_reason');
    if (!/^[A-Za-z0-9_-]{8,64}$/.test(key)) throw badRequest('invalid_key');

    return this.tx(async (c) => {
      const wallet = await this.lockWallet(c, uid);
      const idem = `grant:${key}`;
      const existing = await c.query('SELECT id FROM ledger_transactions WHERE idempotency_key = $1', [idem]);
      if (existing.rows[0]) return { balance: await this.balanceOf(c, wallet), replayed: true };

      const promo = await this.platformAccount(c, 'platform:promo');
      const t = await c.query(
        `INSERT INTO ledger_transactions(idempotency_key, type, metadata) VALUES ($1,'promo_grant',$2) RETURNING id`,
        [idem, JSON.stringify({ by: adminUid, to: uid, reason: why })],
      );
      const id = t.rows[0].id;
      await c.query(`INSERT INTO ledger_entries(transaction_id, account_id, direction, amount) VALUES ($1,$2,'debit',$3)`, [id, promo, amount]);
      await c.query(`INSERT INTO ledger_entries(transaction_id, account_id, direction, amount) VALUES ($1,$2,'credit',$3)`, [id, wallet, amount]);
      return { balance: await this.balanceOf(c, wallet), replayed: false };
    });
  }

  async sendGift(uid: string, roomId: string, giftId: string, recipientUid: string, key: string): Promise<SendGiftResult> {
    if (!/^[A-Za-z0-9_-]{8,64}$/.test(key)) throw badRequest('invalid_key');
    if (uid === recipientUid) throw badRequest('cannot_gift_self');

    return this.tx(async (c) => {
      const wallet = await this.lockWallet(c, uid);
      const idem = `gift:${uid}:${key}`;

      // إعادة الطلب بنفس المفتاح: نفس النتيجة دون خصم ثانٍ
      const prior = await c.query(
        `SELECT m.id AS msg_id FROM ledger_transactions t
           JOIN room_gifts g ON g.ledger_tx_id = t.id
           JOIN room_messages m ON m.room_id = g.room_id AND m.kind = 'gift' AND (m.meta->>'giftRowId')::bigint = g.id
          WHERE t.idempotency_key = $1`,
        [idem],
      );
      if (prior.rows[0]) {
        return { ok: true as const, giftMessageId: Number(prior.rows[0].msg_id), balance: await this.balanceOf(c, wallet), replayed: true };
      }

      const room = await c.query(`SELECT id FROM rooms WHERE id = $1 AND status = 'open'`, [roomId]);
      if (!room.rows[0]) throw notFound('room_not_found');
      if ((await c.query('SELECT 1 FROM room_bans WHERE room_id = $1 AND uid = $2', [roomId, uid])).rowCount) {
        throw forbidden('banned_from_room');
      }
      const isOwner = (await c.query(`SELECT 1 FROM room_roles WHERE room_id = $1 AND uid = $2 AND role = 'owner'`, [roomId, uid])).rowCount;
      const hasAccess = isOwner || (await c.query('SELECT 1 FROM room_access WHERE room_id = $1 AND uid = $2', [roomId, uid])).rowCount;
      if (!hasAccess) throw forbidden('join_required');

      // المستلم يجب أن يكون على المنصة الآن
      if (!(await c.query('SELECT 1 FROM room_seats WHERE room_id = $1 AND uid = $2', [roomId, recipientUid])).rowCount) {
        throw conflict('recipient_not_on_stage');
      }

      const g = await c.query('SELECT id, name_ar, name_en, price_coins FROM gifts WHERE id = $1 AND active', [giftId]);
      if (!g.rows[0]) throw notFound('gift_not_found');
      const price = Number(g.rows[0].price_coins);

      const bal = await this.balanceOf(c, wallet);
      if (bal < price) throw paymentRequired('insufficient_funds');

      const sink = await this.platformAccount(c, 'platform:gift_value');
      const t = await c.query(
        `INSERT INTO ledger_transactions(idempotency_key, type, metadata) VALUES ($1,'gift_sent',$2) RETURNING id`,
        [idem, JSON.stringify({ roomId, from: uid, to: recipientUid, giftId, price })],
      );
      const txId = t.rows[0].id;
      await c.query(`INSERT INTO ledger_entries(transaction_id, account_id, direction, amount) VALUES ($1,$2,'debit',$3)`, [txId, wallet, price]);
      await c.query(`INSERT INTO ledger_entries(transaction_id, account_id, direction, amount) VALUES ($1,$2,'credit',$3)`, [txId, sink, price]);

      const rg = await c.query(
        `INSERT INTO room_gifts(room_id, sender_uid, recipient_uid, gift_id, price_coins, ledger_tx_id)
         VALUES ($1,$2,$3,$4,$5,$6) RETURNING id`,
        [roomId, uid, recipientUid, giftId, price, txId],
      );
      const meta = {
        giftRowId: Number(rg.rows[0].id), giftId, recipientUid, priceCoins: price,
        nameAr: g.rows[0].name_ar, nameEn: g.rows[0].name_en,
      };
      const msg = await c.query(
        `INSERT INTO room_messages(room_id, uid, body, kind, meta) VALUES ($1,$2,$3,'gift',$4) RETURNING id`,
        [roomId, uid, giftId, JSON.stringify(meta)],
      );
      return { ok: true as const, giftMessageId: Number(msg.rows[0].id), balance: bal - price, replayed: false };
    });
  }
}
