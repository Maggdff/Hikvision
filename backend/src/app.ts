import Fastify, { type FastifyRequest } from 'fastify';
import type pg from 'pg';
import type { TokenVerifier } from './auth.js';
import { HttpError, badRequest } from './errors.js';
import { RoomService } from './rooms/service.js';
import type { VoiceProvider } from './voice.js';

export interface AppDeps {
  pool: pg.Pool;
  verifier: TokenVerifier;
  voice: VoiceProvider;
}

declare module 'fastify' {
  interface FastifyRequest {
    uid: string;
  }
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const UID = /^[A-Za-z0-9_-]{1,128}$/;

function uuidParam(v: string): string {
  if (!UUID.test(v)) throw badRequest('invalid_id');
  return v;
}
function uidParam(v: string): string {
  if (!UID.test(v)) throw badRequest('invalid_uid');
  return v;
}

export function buildApp(deps?: AppDeps) {
  const app = Fastify({ logger: process.env.NODE_ENV !== 'test' });

  app.get('/health', async () => ({ status: 'ok' }));

  app.setErrorHandler((err, req, reply) => {
    if (err instanceof HttpError) return reply.status(err.status).send({ error: err.code });
    if ((err as { validation?: unknown }).validation) return reply.status(400).send({ error: 'invalid_request' });
    req.log.error(err);
    return reply.status(500).send({ error: 'internal_error' });
  });

  if (!deps) return app;

  const rooms = new RoomService(deps.pool, deps.voice, (e) => app.log.error({ err: e }, 'voice sync failed'));

  // كل مسارات /v1 تتطلب Firebase ID Token صالحاً
  app.register(async (api) => {
    api.decorateRequest('uid', '');
    api.addHook('preHandler', async (req: FastifyRequest) => {
      const h = req.headers.authorization;
      if (!h?.startsWith('Bearer ')) throw new HttpError(401, 'unauthenticated');
      try {
        req.uid = (await deps.verifier.verify(h.slice(7))).uid;
      } catch {
        throw new HttpError(401, 'unauthenticated');
      }
    });

    api.post('/rooms', async (req, reply) => {
      const b = (req.body ?? {}) as { title?: unknown; topic?: unknown; country?: unknown; password?: unknown };
      if (typeof b.title !== 'string') throw badRequest('invalid_title');
      if (b.topic !== undefined && b.topic !== null && typeof b.topic !== 'string') throw badRequest('invalid_topic');
      if (b.country !== undefined && b.country !== null && typeof b.country !== 'string') throw badRequest('invalid_country');
      if (b.password !== undefined && b.password !== null && typeof b.password !== 'string') throw badRequest('invalid_password');
      const room = await rooms.createRoom(
        req.uid,
        b.title,
        (b.topic as string | null | undefined) ?? null,
        (b.country as string | null | undefined) ?? null,
        (b.password as string | null | undefined) ?? null,
      );
      return reply.status(201).send(room);
    });

    api.get<{ Querystring: { country?: string } }>('/rooms', async (req) => ({
      rooms: await rooms.listOpenRooms(50, req.query.country || null),
    }));

    api.get<{ Params: { id: string } }>('/rooms/:id', async (req) =>
      rooms.getRoomState(req.uid, uuidParam(req.params.id)));

    api.post<{ Params: { id: string } }>('/rooms/:id/join', async (req) => {
      const b = (req.body ?? {}) as { password?: unknown };
      if (b.password !== undefined && b.password !== null && typeof b.password !== 'string') throw badRequest('invalid_password');
      return rooms.join(req.uid, uuidParam(req.params.id), (b.password as string | null | undefined) ?? null);
    });

    api.post<{ Params: { id: string } }>('/rooms/:id/messages', async (req, reply) => {
      const b = (req.body ?? {}) as { body?: unknown };
      if (typeof b.body !== 'string') throw badRequest('invalid_message');
      return reply.status(201).send(await rooms.postMessage(req.uid, uuidParam(req.params.id), b.body));
    });

    api.get<{ Params: { id: string }; Querystring: { after?: string } }>('/rooms/:id/messages', async (req) => {
      const raw = req.query.after;
      let after: number | null = null;
      if (raw !== undefined) {
        if (!/^[0-9]{1,15}$/.test(raw)) throw badRequest('invalid_after');
        after = Number(raw);
      }
      return { messages: await rooms.listMessages(req.uid, uuidParam(req.params.id), after) };
    });

    api.post<{ Params: { id: string } }>('/rooms/:id/close', async (req) =>
      rooms.closeRoom(req.uid, uuidParam(req.params.id)));

    api.post<{ Params: { id: string } }>('/rooms/:id/seat-requests', async (req, reply) =>
      reply.status(201).send(await rooms.requestSeat(req.uid, uuidParam(req.params.id))));

    api.get<{ Params: { id: string } }>('/rooms/:id/seat-requests', async (req) => ({
      requests: await rooms.listSeatRequests(req.uid, uuidParam(req.params.id)),
    }));

    api.post<{ Params: { id: string; reqId: string; decision: string } }>(
      '/rooms/:id/seat-requests/:reqId/:decision',
      async (req) => {
        const { decision } = req.params;
        if (decision !== 'approve' && decision !== 'reject') throw badRequest('invalid_decision');
        return rooms.decideSeatRequest(
          req.uid, uuidParam(req.params.id), uuidParam(req.params.reqId), decision === 'approve');
      },
    );

    api.post<{ Params: { id: string } }>('/rooms/:id/seat/leave', async (req) =>
      rooms.leaveSeat(req.uid, uuidParam(req.params.id)));

    api.post<{ Params: { id: string; uid: string } }>('/rooms/:id/members/:uid/mute', async (req) =>
      rooms.mute(req.uid, uuidParam(req.params.id), uidParam(req.params.uid)));

    api.post<{ Params: { id: string; uid: string } }>('/rooms/:id/members/:uid/kick', async (req) => {
      const ban = ((req.body ?? {}) as { ban?: unknown }).ban === true;
      return rooms.kick(req.uid, uuidParam(req.params.id), uidParam(req.params.uid), ban);
    });

    api.put<{ Params: { id: string; uid: string } }>('/rooms/:id/moderators/:uid', async (req, reply) => {
      await rooms.setModerator(req.uid, uuidParam(req.params.id), uidParam(req.params.uid), true);
      return reply.status(204).send();
    });

    api.delete<{ Params: { id: string; uid: string } }>('/rooms/:id/moderators/:uid', async (req, reply) => {
      await rooms.setModerator(req.uid, uuidParam(req.params.id), uidParam(req.params.uid), false);
      return reply.status(204).send();
    });
  }, { prefix: '/v1' });

  return app;
}
