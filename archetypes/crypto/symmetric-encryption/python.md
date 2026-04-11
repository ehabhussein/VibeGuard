---
schema_version: 1
archetype: crypto/symmetric-encryption
language: python
principles_file: _principles.md
libraries:
  preferred: cryptography
  acceptable:
    - pynacl
    - boto3 (for AWS KMS)
    - google-cloud-kms
  avoid:
    - name: pycrypto
      reason: Unmaintained since 2013, unpatched CVEs, superseded by `cryptography`.
    - name: pycryptodome with ECB
      reason: The library is fine; the mode is not. Every real bug report blaming pycryptodome is actually blaming ECB.
    - name: hashlib.sha256(password).digest() as a key
      reason: SHA-256 is a hash, not a KDF. Use HKDF or Argon2id.
minimum_versions:
  python: "3.11"
---

# Symmetric Encryption — Python

## Library choice
`cryptography` (the package literally named `cryptography`, maintained by the PyCA) is the correct default. Its `hazmat` layer exposes `AESGCM` and `ChaCha20Poly1305` with an interface that makes the safe usage the obvious usage and the unsafe usage actively awkward. `pynacl` is a great alternative if you want NaCl's opinionated API — it's harder to misuse but slightly less flexible. For anything touching a cloud KMS, prefer the cloud SDK's envelope-encryption helpers (`boto3` KMS `encrypt`/`decrypt` for small blobs, or `generate_data_key` for large ones) — the master key never leaves the KMS. Steer clear of `pycrypto` (abandoned) and be careful with `pycryptodome`: the library is fine, but it's old enough that plenty of bad examples exist for it online.

## Reference implementation
```python
from __future__ import annotations
from dataclasses import dataclass
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
import os

_CURRENT_VERSION = 1


@dataclass(frozen=True, slots=True)
class Envelope:
    version: int        # key id for rotation
    nonce: bytes        # 12 bytes, unique per (key, message)
    ciphertext: bytes   # includes the 16-byte tag at the end


class RecordCryptographer:
    def __init__(self, key: bytes) -> None:
        if len(key) not in (16, 24, 32):
            raise ValueError("key must be 16, 24, or 32 bytes (AES-128/192/256)")
        self._aead = AESGCM(key)

    def encrypt(self, plaintext: bytes, associated_data: bytes) -> Envelope:
        nonce = os.urandom(12)
        ct = self._aead.encrypt(nonce, plaintext, associated_data)
        return Envelope(
            version=_CURRENT_VERSION,
            nonce=nonce,
            ciphertext=ct,
        )

    def decrypt(self, envelope: Envelope, associated_data: bytes) -> bytes:
        # Raises InvalidTag on any tampering or wrong AAD — never swallow.
        return self._aead.decrypt(
            envelope.nonce, envelope.ciphertext, associated_data,
        )
```

## Language-specific gotchas
- `AESGCM.encrypt` returns `ciphertext || tag` as a single `bytes` object. Don't try to split them by hand — pass the whole blob back into `decrypt`. This is a frequent bug in code ported from other libraries that separate the tag.
- `os.urandom(12)` is the correct nonce source. It's a CSPRNG-backed syscall. `random.randbytes`, `numpy.random`, and anything from the `random` module are *not* cryptographic RNGs and will get you ranked on every audit.
- A 32-byte key is AES-256; a 16-byte key is AES-128; a 24-byte key is AES-192. You almost always want 32. Enforce the length check in the constructor — sanity-checking here catches the "I passed a password string instead of key bytes" bug at startup.
- The key comes from `SecretsProvider` (see `persistence/secrets-handling`). In practice: `AESGCM(settings.master_key.get_secret_value())` where `master_key` is a `SecretBytes` or the raw output of a KDF like HKDF over a KMS-held data key. Never pass a hardcoded string literal.
- `associated_data` must be identical on encrypt and decrypt. Pass something stable: `f"order:{order_id}:v{schema_version}".encode()`. This binds the ciphertext to its record and makes cross-record replays fail the tag check.
- The `Envelope` dataclass is `frozen=True, slots=True` for two reasons: frozen stops accidental mutation of the nonce between encrypt and persist, and slots prevents adding a `plaintext` attribute "temporarily for debugging" that would then get pickled / logged / serialized by mistake.

## Tests to write
- Round-trip: encrypt a known plaintext, decrypt, assert equal.
- AAD binding: encrypt with `aad=b"A"`, decrypt with `aad=b"B"`, assert `InvalidTag` is raised.
- Tag tampering: flip a bit in `envelope.ciphertext`, assert `InvalidTag` on decrypt.
- Key length enforcement: constructing `RecordCryptographer(b"short")` raises `ValueError`.
- Nonce uniqueness smoke: call `encrypt` 1,000 times and assert 1,000 distinct nonces (cheap sanity check against a mis-wired RNG).
- Rotation path: encrypt with key v1, swap in key v2, decrypt v1 via a version dispatch, assert the right plaintext comes back.
- No accidental logging: assert `repr(envelope)` does not include the plaintext (it shouldn't — plaintext isn't stored on the envelope — but a regression test catches a future "helpful debugging" change).
