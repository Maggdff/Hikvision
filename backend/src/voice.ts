import { AccessToken, RoomServiceClient } from 'livekit-server-sdk';

export interface VoiceProvider {
  /** رمز اتصال قصير العمر؛ canPublish=true فقط لمن يجلس على مقعد. */
  issueToken(p: { roomId: string; uid: string; canPublish: boolean }): Promise<string>;
  setCanPublish(roomId: string, uid: string, canPublish: boolean): Promise<void>;
  muteParticipant(roomId: string, uid: string): Promise<void>;
  removeParticipant(roomId: string, uid: string): Promise<void>;
  closeRoom(roomId: string): Promise<void>;
  readonly publicUrl: string;
}

const TOKEN_TTL = '15m';

export class LiveKitProvider implements VoiceProvider {
  private readonly client: RoomServiceClient;

  constructor(
    private readonly url: string,
    private readonly apiKey: string,
    private readonly apiSecret: string,
  ) {
    // RoomServiceClient يتوقع http(s)؛ العميل يتصل عبر wss
    this.client = new RoomServiceClient(url.replace(/^ws/, 'http'), apiKey, apiSecret);
  }

  get publicUrl(): string {
    return this.url;
  }

  async issueToken({ roomId, uid, canPublish }: { roomId: string; uid: string; canPublish: boolean }) {
    const at = new AccessToken(this.apiKey, this.apiSecret, { identity: uid, ttl: TOKEN_TTL });
    at.addGrant({
      roomJoin: true,
      room: roomId,
      canSubscribe: true,
      canPublish,
      canPublishSources: canPublish ? [2 /* MICROPHONE */] : [],
      canPublishData: false,
    });
    return at.toJwt();
  }

  async setCanPublish(roomId: string, uid: string, canPublish: boolean) {
    await this.client.updateParticipant(roomId, uid, {
      permission: {
        canSubscribe: true,
        canPublish,
        canPublishSources: canPublish ? [2] : [],
        canPublishData: false,
      },
    });
  }

  async muteParticipant(roomId: string, uid: string) {
    const p = await this.client.getParticipant(roomId, uid);
    for (const t of p.tracks) {
      if (!t.muted) await this.client.mutePublishedTrack(roomId, uid, t.sid, true);
    }
  }

  async removeParticipant(roomId: string, uid: string) {
    await this.client.removeParticipant(roomId, uid);
  }

  async closeRoom(roomId: string) {
    await this.client.deleteRoom(roomId);
  }
}
