#!/usr/bin/env python3
"""Reference implementation of the request signing (same as GateAuth.cs and firmware).
Usage:  python3 sign_check.py            -> prints the reference vector
        python3 sign_check.py KEYHEX METHOD PATH DEVICE SEQ TS NONCE BODY  -> signature
"""
import hashlib, hmac, sys

def canonical(method, path, device, seq, ts, nonce, body: bytes) -> str:
    return f"{method.upper()}\n{path}\n{device}\n{seq}\n{ts}\n{nonce}\n{hashlib.sha256(body).hexdigest()}"

def sign(key: bytes, method, path, device, seq, ts, nonce, body: bytes) -> str:
    return hmac.new(key, canonical(method, path, device, seq, ts, nonce, body).encode(), hashlib.sha256).hexdigest()

if __name__ == "__main__":
    if len(sys.argv) == 1:
        key = bytes(range(32))
        body = b'{"uid":"04A1B2C3"}'
        print(sign(key, "POST", "/api/access/check", "ESP1", 7, 1800000000123, "0123456789abcdef", body))
        print("expected: 698a6bfb075693abd6a7e1553ef3dba3bbee17b293023dbf3e89584c633ae08f")
    else:
        k, m, p, d, s, t, n, b = sys.argv[1:9]
        print(sign(bytes.fromhex(k), m, p, d, s, t, n, b.encode()))
