import { initializeApp, getApps } from 'firebase-admin/app';
import { getAuth } from 'firebase-admin/auth';

export interface TokenVerifier {
  /** يتحقق من Firebase ID Token ويُرجع uid، أو يرمي استثناءً إن كان غير صالح. */
  verify(token: string): Promise<{ uid: string }>;
}

/** التحقق من رموز المستخدمين لا يحتاج مفتاح خدمة؛ يكفي معرّف المشروع. */
export class FirebaseTokenVerifier implements TokenVerifier {
  constructor(projectId: string) {
    if (getApps().length === 0) initializeApp({ projectId });
  }

  async verify(token: string): Promise<{ uid: string }> {
    const decoded = await getAuth().verifyIdToken(token);
    return { uid: decoded.uid };
  }
}
