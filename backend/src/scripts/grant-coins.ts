/**
 * منح عملات ترويجية لمستخدم عبر الدفتر المحاسبي (لا تعديل مباشر للرصيد).
 * الاستخدام: DATABASE_URL=... npm run grant -- <firebase-uid> <amount> "<reason>" [key]
 * المفتاح اختياري؛ أعد استعمال نفس المفتاح لتأمين عدم تكرار المنحة عند إعادة التشغيل.
 */
import { randomUUID } from 'node:crypto';
import pg from 'pg';
import { EconomyService } from '../economy/service.js';

const [uid, amountRaw, reason, keyArg] = process.argv.slice(2);
const url = process.env.DATABASE_URL;
if (!url || !uid || !amountRaw || !reason) {
  console.error('usage: DATABASE_URL=... npm run grant -- <firebase-uid> <amount> "<reason>" [key]');
  process.exit(2);
}
const pool = new pg.Pool({ connectionString: url, max: 1 });
try {
  const key = keyArg ?? randomUUID();
  const r = await new EconomyService(pool).grant('cli', uid, Number(amountRaw), reason, key);
  console.log(JSON.stringify({ uid, ...r, key }));
} finally {
  await pool.end();
}
