---
schema_version: 1
archetype: io/unsafe-deserialization
language: python
principles_file: _principles.md
libraries:
  preferred: pydantic
  acceptable:
    - msgspec (for performance-sensitive endpoints)
    - marshmallow
  avoid:
    - name: pickle (for network or user-controlled input)
      reason: pickle.loads on attacker-controlled bytes is remote code execution by design — there is no safe configuration.
    - name: yaml.load without SafeLoader
      reason: Default loaders instantiate arbitrary Python objects from !!python/object tags.
    - name: shelve, marshal, dill
      reason: All of these are pickle-family or worse. Same RCE exposure.
    - name: json.loads followed by ad-hoc dict access
      reason: Parses safely but then business logic walks untyped dicts; validation becomes impossible and key errors surface at runtime.
minimum_versions:
  python: "3.11"
---

# Unsafe Deserialization Defense — Python

## Library choice
`pydantic` (v2) is the correct default. It parses and validates in one step: construct a `BaseModel` from raw bytes or a dict and every field gets type-checked, range-checked (via `Field` constraints), and cross-checked (via `@model_validator`) before your code ever sees it. `msgspec` is the right pick if you need near-orjson parsing speed and are willing to use a smaller validation vocabulary. `marshmallow` is an older acceptable alternative for codebases already standardized on it. Plain `json.loads` is safe at the *parse* level but leaves you with an untyped `dict` that has to be validated separately — don't stop at the parse step.

## Reference implementation
```python
from __future__ import annotations
from decimal import Decimal
from typing import Annotated
from pydantic import BaseModel, ConfigDict, Field, ValidationError

MAX_BODY_BYTES = 64 * 1024


class LineItem(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    sku: Annotated[str, Field(min_length=1, max_length=64, pattern=r"^[A-Z0-9-]+$")]
    quantity: Annotated[int, Field(ge=1, le=1000)]


class CreateOrderRequest(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    customer_id: Annotated[str, Field(min_length=1, max_length=64)]
    items: Annotated[list[LineItem], Field(min_length=1, max_length=500)]
    total_cents: Annotated[Decimal, Field(ge=0, le=Decimal("10000000"))]


class PayloadTooLarge(Exception):
    pass


def parse_create_order(body: bytes) -> CreateOrderRequest:
    if len(body) > MAX_BODY_BYTES:
        raise PayloadTooLarge(f"body {len(body)}B exceeds {MAX_BODY_BYTES}B")
    try:
        # model_validate_json parses AND validates in one step.
        return CreateOrderRequest.model_validate_json(body)
    except ValidationError:
        # Let it propagate — the framework maps it to 400.
        # Do NOT catch and retry with a different parser.
        raise
```

## Language-specific gotchas
- `pickle.loads(body)` on anything that came over the network is *the* unsafe-deserialization anti-pattern in Python. There is no safe flag, no sandbox, no `Unpickler.find_class` hack that makes it safe for attacker-controlled input. If you see pickle reaching a network handler, the fix is to replace the format, not to "lock it down."
- `yaml.load(data)` with the default loader instantiates arbitrary Python objects via `!!python/object/apply:os.system` style tags. Always use `yaml.safe_load` (or PyYAML's `SafeLoader`). Better: don't accept YAML from untrusted sources at all — its surface area is much larger than JSON's for negligible benefit.
- `pydantic`'s `extra="forbid"` is load-bearing. Without it, extra fields in the payload are silently ignored — which defeats the "closed schema" principle and also hides bugs where a client sent the wrong field name and expected it to work.
- `frozen=True` prevents mutation after construction, which matters because Pydantic models often get stored on a request-scoped object and then passed deep into business logic. Immutability here eliminates whole classes of "we mutated the request object and then audit-logged the stale version" bugs.
- `Decimal` for money. Never `float`. Pydantic handles `Decimal` natively and will reject JSON numbers that can't be represented exactly.
- `model_validate_json` is faster and stricter than `model_validate(json.loads(...))` because it parses and validates in a single pass with type-directed decoding. Use it.
- Content-length caps happen at the framework layer (`CONTENT_LENGTH` in ASGI, `client_max_body_size` in nginx) *and* in the parser. Don't rely on only one — a misconfigured reverse proxy shouldn't let megabytes through to the parser.
- Never pass `model_validate` a generic `dict` coming from a Python-side cache without thinking about where that dict originated. "It's internal" is often "an attacker could reach the cache via another route."

## Tests to write
- Happy-path: valid JSON body parses and returns a `CreateOrderRequest`.
- Missing required field: `{"customer_id":"c1"}` raises `ValidationError`.
- Extra field rejected: `{..., "is_admin": true}` raises `ValidationError` (regression for `extra="forbid"`).
- Over-size body: 65 KB of valid JSON raises `PayloadTooLarge` *before* Pydantic sees it.
- Pattern mismatch: SKU containing `!` raises `ValidationError`.
- Quantity out of range: `quantity=0` and `quantity=1001` both raise.
- No fallback parser: feed malformed JSON and assert `parse_create_order` raises — does not try `yaml.load`, does not try `pickle.loads`, does not return a default.
- Pickle regression: a repo-wide grep test that fails if `import pickle` appears in any module under `handlers/` or `api/`.
- YAML safety: if yaml is imported at all, a grep test that fails if `yaml.load(` appears without `SafeLoader`.
