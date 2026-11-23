import { randomBytes, scryptSync, timingSafeEqual } from 'node:crypto';

/** كلمة سر الغرفة: 6 أرقام بالضبط. */
export const isValidRoomPassword = (p: string): boolean => /^[0-9]{6}$/.test(p);

export function hashRoomPassword(password: string): string {
  const salt = randomBytes(16);
  const hash = scryptSync(password, salt, 32);
  return `scrypt$${salt.toString('hex')}$${hash.toString('hex')}`;
}

export function verifyRoomPassword(password: string, stored: string): boolean {
  const [scheme, saltHex, hashHex] = stored.split('$');
  if (scheme !== 'scrypt' || !saltHex || !hashHex) return false;
  const expected = Buffer.from(hashHex, 'hex');
  const actual = scryptSync(password, Buffer.from(saltHex, 'hex'), expected.length);
  return timingSafeEqual(actual, expected);
}
