export class HttpError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
  ) {
    super(code);
  }
}

export const notFound = (code = 'not_found') => new HttpError(404, code);
export const forbidden = (code = 'forbidden') => new HttpError(403, code);
export const conflict = (code: string) => new HttpError(409, code);
export const paymentRequired = (code: string) => new HttpError(402, code);
export const tooManyRequests = (code: string) => new HttpError(429, code);
export const badRequest = (code: string) => new HttpError(400, code);
