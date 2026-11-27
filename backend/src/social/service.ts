import pg from 'pg';
import { badRequest } from '../errors.js';

export interface FollowCounts {
  followers: number;
  following: number;
}

export interface Banner {
  id: number;
  title: string;
  subtitle: string | null;
}

/** المتابعة: علاقة بسيطة وآمنة للتكرار (متابعة موجودة ← لا شيء يتغير). */
export class SocialService {
  constructor(private readonly pool: pg.Pool) {}

  async follow(me: string, target: string): Promise<void> {
    if (me === target) throw badRequest('cannot_follow_self');
    await this.pool.query(
      `INSERT INTO follows(follower_uid, followee_uid) VALUES ($1, $2) ON CONFLICT DO NOTHING`,
      [me, target],
    );
  }

  async unfollow(me: string, target: string): Promise<void> {
    await this.pool.query(`DELETE FROM follows WHERE follower_uid = $1 AND followee_uid = $2`, [me, target]);
  }

  async isFollowing(me: string, target: string): Promise<boolean> {
    const r = await this.pool.query(`SELECT 1 FROM follows WHERE follower_uid = $1 AND followee_uid = $2`, [me, target]);
    return r.rowCount === 1;
  }

  async counts(uid: string): Promise<FollowCounts> {
    const r = await this.pool.query(
      `SELECT (SELECT count(*) FROM follows WHERE followee_uid = $1)::int AS followers,
              (SELECT count(*) FROM follows WHERE follower_uid = $1)::int AS following`,
      [uid],
    );
    return { followers: r.rows[0].followers, following: r.rows[0].following };
  }

  /** اللافتات الفعالة الآن فقط (ضمن نافذة البدء والانتهاء إن وُجدت). */
  async activeBanners(): Promise<Banner[]> {
    const r = await this.pool.query(
      `SELECT id, title, subtitle FROM banners
        WHERE active AND (starts_at IS NULL OR starts_at <= now()) AND (ends_at IS NULL OR ends_at > now())
        ORDER BY sort_order, id LIMIT 10`,
    );
    return r.rows.map((x) => ({ id: Number(x.id), title: x.title, subtitle: x.subtitle ?? null }));
  }
}
