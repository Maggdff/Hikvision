import pg from 'pg';
import { buildApp } from './app.js';
import { FirebaseTokenVerifier } from './auth.js';
import { migrate } from './db/migrate.js';
import { LiveKitProvider } from './voice.js';

function required(name: string): string {
  const v = process.env[name];
  if (!v) throw new Error(`${name} is required`);
  return v;
}

// كل الأسرار من متغيرات البيئة فقط (Secret Manager / GitHub Secrets)، لا شيء داخل الكود
const databaseUrl = required('DATABASE_URL');

// تشغيل الـ Migrations عند الإقلاع اختياري (RUN_MIGRATIONS=true)؛ القفل يمنع تعارض النسخ
if (process.env.RUN_MIGRATIONS === 'true') {
  const applied = await migrate(databaseUrl);
  console.log(applied.length ? `migrations applied: ${applied.join(', ')}` : 'migrations up to date');
}

const pool = new pg.Pool({ connectionString: databaseUrl, max: Number(process.env.DB_POOL_MAX ?? 10) });
const app = buildApp({
  pool,
  verifier: new FirebaseTokenVerifier(required('FIREBASE_PROJECT_ID')),
  voice: new LiveKitProvider(required('LIVEKIT_URL'), required('LIVEKIT_API_KEY'), required('LIVEKIT_API_SECRET')),
  assetsBaseUrl: process.env.ASSETS_BASE_URL ?? '',
  adminUids: (process.env.ADMIN_UIDS ?? '').split(',').map((s) => s.trim()).filter(Boolean),
});

// Cloud Run يرسل SIGTERM قبل الإيقاف: أنهِ الطلبات الجارية ثم أغلق الاتصالات
for (const sig of ['SIGTERM', 'SIGINT'] as const) {
  process.on(sig, async () => {
    await app.close();
    await pool.end();
    process.exit(0);
  });
}

const port = Number(process.env.PORT ?? 8080);
app.listen({ port, host: '0.0.0.0' }).catch((err) => {
  app.log.error(err);
  process.exit(1);
});
