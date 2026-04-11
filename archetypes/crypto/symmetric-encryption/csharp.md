---
schema_version: 1
archetype: crypto/symmetric-encryption
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.Security.Cryptography.AesGcm
  acceptable:
    - System.Security.Cryptography.ChaCha20Poly1305
    - Azure.Security.KeyVault.Keys.Cryptography
  avoid:
    - name: System.Security.Cryptography.Aes (raw)
      reason: Ships unauthenticated by default; every direct consumer is one MAC-forgetting bug away from a padding oracle.
    - name: System.Security.Cryptography.AesCryptoServiceProvider
      reason: Legacy, Windows-only, no authenticated mode, deprecated.
    - name: TripleDES / DES / RC2 / RC4
      reason: Obsolete, weak, or both. Allowed only for interop with a system you cannot change.
minimum_versions:
  dotnet: "10.0"
---

# Symmetric Encryption — C#

## Library choice
`System.Security.Cryptography.AesGcm` is the stock AEAD primitive and the correct default. It is a thin wrapper over the OS crypto provider, uses 12-byte nonces, produces a 16-byte tag, and fails loudly on tag mismatch. `ChaCha20Poly1305` is the right pick if you need a constant-time software implementation (it's a drop-in shape-compatible replacement). For anything touching a KMS-held master key, `Azure.Security.KeyVault.Keys.Cryptography` (or the AWS/GCP equivalent) keeps the key out of process entirely and is the right answer for high-value data. Avoid `Aes` without AEAD — not because it's wrong in principle, but because every real codebase that reaches for it eventually ships a MAC-forgetting variant.

## Reference implementation
```csharp
using System.Security.Cryptography;

public sealed class Envelope
{
    public required byte Version { get; init; }    // key-id prefix for rotation
    public required byte[] Nonce { get; init; }    // 12 bytes, unique per (key, message)
    public required byte[] Ciphertext { get; init; }
    public required byte[] Tag { get; init; }      // 16 bytes
}

public sealed class RecordCryptographer(ReadOnlyMemory<byte> key)
{
    private const byte CurrentVersion = 1;
    private readonly AesGcm _cipher = new(key.Span, tagSizeInBytes: 16);

    public Envelope Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize); // 12
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16
        _cipher.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return new Envelope
        {
            Version = CurrentVersion,
            Nonce = nonce,
            Ciphertext = ciphertext,
            Tag = tag,
        };
    }

    public byte[] Decrypt(Envelope env, ReadOnlySpan<byte> associatedData)
    {
        // CryptographicException on tag mismatch — never swallow it.
        var plaintext = new byte[env.Ciphertext.Length];
        _cipher.Decrypt(env.Nonce, env.Ciphertext, env.Tag, plaintext, associatedData);
        return plaintext;
    }
}
```

## Language-specific gotchas
- `AesGcm` is `IDisposable` in modern .NET — it holds an unmanaged handle. Treat it as a long-lived singleton behind the `Cryptographer`, not a per-call allocation. Disposing it on every call is correctness-safe but silently tanks throughput.
- `RandomNumberGenerator.GetBytes(12)` is the correct nonce source — a CSPRNG. `new Random().NextBytes(buf)` is not a CSPRNG and is catastrophically wrong here.
- Never pass `key.Span` that came from a `string`. `"mykey12345..."u8` is not a key; even base64-of-ASCII is not a key. The key comes from `SecretsProvider` (see `persistence/secrets-handling`), which pulls the actual bytes from the KMS / env / secrets store.
- `associatedData` must be *identical* on encrypt and decrypt. Pass something stable like `$"order:{orderId}:v{schemaVersion}"` encoded as UTF-8. Cross-record replay attacks fail the tag check when you do this right.
- The `Envelope.Version` byte exists specifically for rotation. When you rotate, the decrypt path reads the version, looks up the matching key, and decrypts; the encrypt path always writes the current version. Don't skip the byte "for now" — adding it later requires a migration over every row.
- Do not wrap `Decrypt` in `try { ... } catch (CryptographicException) { return null; }` and treat the null as "skip this row." That's exactly the fall-back-to-unencrypted pattern the principles ban.

## Tests to write
- Round-trip: encrypt a known plaintext, decrypt it, assert bytes equal.
- Associated-data binding: encrypt with `aad=A`, decrypt with `aad=B`, assert `CryptographicException` (not success, not a wrong-but-valid plaintext).
- Tag tampering: encrypt, flip a bit in the ciphertext, decrypt, assert `CryptographicException`.
- Nonce uniqueness: generate 1,000 nonces via the cryptographer and assert zero collisions (cheap probabilistic smoke test, catches a mis-wired `Random`).
- Rotation path: encrypt with key v1, swap the cryptographer to v2, decrypt v1 ciphertext via the version dispatch, assert success.
- Key source: assert that `Cryptographer`'s constructor rejects a zero-filled or all-ASCII key (regression against the "hardcoded string" anti-pattern).
