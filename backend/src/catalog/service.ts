import type pg from 'pg';
import { badRequest, notFound } from '../errors.js';

export type GiftRow = {
  id: string; nameAr: string; nameEn: string; priceCoins: number; tier: string;
  active: boolean; sortOrder: number; iconPath: string | null; effectPath: string | null;
};
export type FrameRow = {
  id: string; nameAr: string; nameEn: string; priceCoins: number;
  active: boolean; sortOrder: number; assetPath: string | null;
};

const ID_RE = /^[a-z0-9_]{2,32}$/;
const PATH_RE = /^[A-Za-z0-9_./-]{1,200}$/;
const TIERS = ['small', 'medium', 'large'];
const MAX_PRICE = 10_000_000;

/** مسار نسبي أو رابط https كامل؛ null/'' يمسح القيمة. */
function cleanPath(v: unknown): string | null | undefined {
  if (v === undefined) return undefined;
  if (v === null || v === '') return null;
  if (typeof v !== 'string') throw badRequest('invalid_path');
  if (/^https:\/\/[^\s]{4,300}$/.test(v)) return v;
  if (!PATH_RE.test(v) || v.includes('..') || v.startsWith('/')) throw badRequest('invalid_path');
  return v;
}

function cleanText(v: unknown, field: string): string | undefined {
  if (v === undefined) return undefined;
  if (typeof v !== 'string' || v.trim().length < 1 || v.trim().length > 40) throw badRequest(`invalid_${field}`);
  return v.trim();
}

function cleanInt(v: unknown, field: string, min: number, max: number): number | undefined {
  if (v === undefined) return undefined;
  if (typeof v !== 'number' || !Number.isInteger(v) || v < min || v > max) throw badRequest(`invalid_${field}`);
  return v;
}

export class CatalogService {
  constructor(private pool: pg.Pool, private baseUrl: string) {}

  /** رابط كامل لملف أصل، مع ?v= ليتجدد كاش الجهاز عند تعديل المشرف. */
  url(path: string | null, updatedAt: Date): string | null {
    if (!path) return null;
    if (path.startsWith('https://')) return path;
    if (!this.baseUrl) return null;
    return `${this.baseUrl.replace(/\/+$/, '')}/${path}?v=${updatedAt.getTime()}`;
  }

  async publicCatalog() {
    const g = await this.pool.query(
      `SELECT id, name_ar, name_en, price_coins, tier, icon_path, effect_path, updated_at
         FROM gifts WHERE active ORDER BY sort_order, price_coins`);
    const f = await this.pool.query(
      `SELECT id, name_ar, name_en, price_coins, asset_path, updated_at
         FROM frames WHERE active ORDER BY sort_order, price_coins`);
    return {
      gifts: g.rows.map((x) => ({
        id: x.id, nameAr: x.name_ar, nameEn: x.name_en, priceCoins: x.price_coins, tier: x.tier,
        iconUrl: this.url(x.icon_path, x.updated_at), effectUrl: this.url(x.effect_path, x.updated_at),
      })),
      frames: f.rows.map((x) => ({
        id: x.id, nameAr: x.name_ar, nameEn: x.name_en, priceCoins: x.price_coins,
        assetUrl: this.url(x.asset_path, x.updated_at),
      })),
    };
  }

  async adminCatalog(): Promise<{ gifts: GiftRow[]; frames: FrameRow[]; assetsBaseUrl: string }> {
    const g = await this.pool.query(
      `SELECT id, name_ar, name_en, price_coins, tier, active, sort_order, icon_path, effect_path FROM gifts ORDER BY sort_order, price_coins`);
    const f = await this.pool.query(
      `SELECT id, name_ar, name_en, price_coins, active, sort_order, asset_path FROM frames ORDER BY sort_order, price_coins`);
    return {
      assetsBaseUrl: this.baseUrl,
      gifts: g.rows.map((x) => ({
        id: x.id, nameAr: x.name_ar, nameEn: x.name_en, priceCoins: x.price_coins, tier: x.tier,
        active: x.active, sortOrder: x.sort_order, iconPath: x.icon_path, effectPath: x.effect_path,
      })),
      frames: f.rows.map((x) => ({
        id: x.id, nameAr: x.name_ar, nameEn: x.name_en, priceCoins: x.price_coins,
        active: x.active, sortOrder: x.sort_order, assetPath: x.asset_path,
      })),
    };
  }

  /** إنشاء أو تعديل هدية. السعر الجديد يسري على الإرسالات القادمة فقط (الدفتر يحفظ السعر وقت كل عملية). */
  async upsertGift(id: string, b: Record<string, unknown>): Promise<void> {
    if (!ID_RE.test(id)) throw badRequest('invalid_id');
    const v = {
      nameAr: cleanText(b.nameAr, 'name_ar'), nameEn: cleanText(b.nameEn, 'name_en'),
      priceCoins: cleanInt(b.priceCoins, 'price', 1, MAX_PRICE), sortOrder: cleanInt(b.sortOrder, 'sort', -1000, 1000),
      iconPath: cleanPath(b.iconPath), effectPath: cleanPath(b.effectPath),
      tier: b.tier === undefined ? undefined : TIERS.includes(b.tier as string) ? (b.tier as string) : (() => { throw badRequest('invalid_tier'); })(),
      active: b.active === undefined ? undefined : typeof b.active === 'boolean' ? b.active : (() => { throw badRequest('invalid_active'); })(),
    };
    const exists = (await this.pool.query('SELECT 1 FROM gifts WHERE id = $1', [id])).rowCount;
    if (!exists) {
      if (!v.nameAr || !v.nameEn || v.priceCoins === undefined) throw badRequest('missing_fields');
      await this.pool.query(
        `INSERT INTO gifts(id, name_ar, name_en, price_coins, tier, active, sort_order, icon_path, effect_path)
         VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)`,
        [id, v.nameAr, v.nameEn, v.priceCoins, v.tier ?? 'small', v.active ?? true, v.sortOrder ?? 0, v.iconPath ?? null, v.effectPath ?? null]);
      return;
    }
    await this.pool.query(
      `UPDATE gifts SET
         name_ar = COALESCE($2, name_ar), name_en = COALESCE($3, name_en), price_coins = COALESCE($4, price_coins),
         tier = COALESCE($5, tier), active = COALESCE($6, active), sort_order = COALESCE($7, sort_order),
         icon_path = CASE WHEN $8::boolean THEN $9 ELSE icon_path END,
         effect_path = CASE WHEN $10::boolean THEN $11 ELSE effect_path END,
         updated_at = now()
       WHERE id = $1`,
      [id, v.nameAr ?? null, v.nameEn ?? null, v.priceCoins ?? null, v.tier ?? null, v.active ?? null, v.sortOrder ?? null,
        v.iconPath !== undefined, v.iconPath ?? null, v.effectPath !== undefined, v.effectPath ?? null]);
  }

  async upsertFrame(id: string, b: Record<string, unknown>): Promise<void> {
    if (!ID_RE.test(id)) throw badRequest('invalid_id');
    const v = {
      nameAr: cleanText(b.nameAr, 'name_ar'), nameEn: cleanText(b.nameEn, 'name_en'),
      priceCoins: cleanInt(b.priceCoins, 'price', 1, MAX_PRICE), sortOrder: cleanInt(b.sortOrder, 'sort', -1000, 1000),
      assetPath: cleanPath(b.assetPath),
      active: b.active === undefined ? undefined : typeof b.active === 'boolean' ? b.active : (() => { throw badRequest('invalid_active'); })(),
    };
    const exists = (await this.pool.query('SELECT 1 FROM frames WHERE id = $1', [id])).rowCount;
    if (!exists) {
      if (!v.nameAr || !v.nameEn || v.priceCoins === undefined) throw badRequest('missing_fields');
      await this.pool.query(
        `INSERT INTO frames(id, name_ar, name_en, price_coins, active, sort_order, asset_path) VALUES ($1,$2,$3,$4,$5,$6,$7)`,
        [id, v.nameAr, v.nameEn, v.priceCoins, v.active ?? true, v.sortOrder ?? 0, v.assetPath ?? null]);
      return;
    }
    const r = await this.pool.query(
      `UPDATE frames SET
         name_ar = COALESCE($2, name_ar), name_en = COALESCE($3, name_en), price_coins = COALESCE($4, price_coins),
         active = COALESCE($5, active), sort_order = COALESCE($6, sort_order),
         asset_path = CASE WHEN $7::boolean THEN $8 ELSE asset_path END, updated_at = now()
       WHERE id = $1`,
      [id, v.nameAr ?? null, v.nameEn ?? null, v.priceCoins ?? null, v.active ?? null, v.sortOrder ?? null,
        v.assetPath !== undefined, v.assetPath ?? null]);
    if (!r.rowCount) throw notFound('frame_not_found');
  }
}
