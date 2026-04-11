---
schema_version: 1
archetype: io/unsafe-deserialization
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.Text.Json
  acceptable:
    - System.Text.Json.Serialization (source-generated)
    - FluentValidation (for post-parse validation)
  avoid:
    - name: System.Runtime.Serialization.Formatters.Binary.BinaryFormatter
      reason: Disabled by default in .NET 9+, RCE-by-design; Microsoft explicitly recommends migration away from it.
    - name: Newtonsoft.Json with TypeNameHandling.All / TypeNameHandling.Auto
      reason: The canonical .NET deserialization-RCE vector; type info in the payload lets attackers instantiate gadget chains.
    - name: NetDataContractSerializer / SoapFormatter / ObjectStateFormatter
      reason: Same class of problem — payload-controlled type instantiation.
minimum_versions:
  dotnet: "10.0"
---

# Unsafe Deserialization Defense — C#

## Library choice
`System.Text.Json` is the stock, correct, and fastest answer. It does not support polymorphic type resolution from the payload by default — you have to opt in with `[JsonDerivedType]` and a discriminator — which is exactly the safe shape. Combine it with source-generated `JsonSerializerContext` for AOT-friendly, allocation-free parsing. `FluentValidation` or a small hand-rolled validator handles the *post*-parse "are the values actually valid" step, because `System.Text.Json` by itself only enforces that the JSON matches the shape of the type, not that the fields are sane. `Newtonsoft.Json` is acceptable only in legacy code and only if `TypeNameHandling` is verified to be `None`.

## Reference implementation
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed record CreateOrderRequest
{
    public required string CustomerId { get; init; }
    public required IReadOnlyList<LineItem> Items { get; init; }
    public required decimal TotalCents { get; init; }
}

public sealed record LineItem
{
    public required string Sku { get; init; }
    public required int Quantity { get; init; }
}

[JsonSerializable(typeof(CreateOrderRequest))]
public partial class OrderJsonContext : JsonSerializerContext { }

public static class OrderPayloadReader
{
    private const long MaxBodyBytes = 64 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 16,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        TypeInfoResolver = OrderJsonContext.Default,
    };

    public static CreateOrderRequest Parse(ReadOnlySpan<byte> body)
    {
        if (body.Length > MaxBodyBytes)
            throw new PayloadTooLargeException(MaxBodyBytes);

        var parsed = JsonSerializer.Deserialize<CreateOrderRequest>(body, Options)
            ?? throw new JsonException("payload was null");

        // Post-parse validation — System.Text.Json won't do this for you.
        if (parsed.Items.Count is 0 or > 500)
            throw new ValidationException("items count out of range");
        if (parsed.TotalCents is < 0 or > 10_000_000)
            throw new ValidationException("total out of range");
        return parsed;
    }
}
```

## Language-specific gotchas
- `BinaryFormatter` is the single most important thing to *not* use. Since .NET 5 it's obsolete, in .NET 9+ it's disabled by default and requires a runtime switch to re-enable. If you find it in the codebase, the migration is not "add the switch" — the migration is to replace it with `System.Text.Json` or a hand-rolled binary format.
- `Newtonsoft.Json`'s `TypeNameHandling` property is the classic RCE primitive. The exploit is a payload like `{"$type":"System.Windows.Data.ObjectDataProvider, ..."}` that instantiates a gadget on parse. Set it to `None` explicitly and grep the repo for any override.
- `System.Text.Json` with a target of `object` or `JsonElement` *is* a form of the same problem — you've moved the type decision from parse-time to use-time, where it's harder to review. Deserialize into a closed record.
- `MaxDepth = 16` (or your chosen cap) is defense in depth against a "billion-nested-arrays" DoS. The default is 64 which is fine for most APIs but too generous if your messages are flat.
- Source-generated `JsonSerializerContext` is not just a perf optimization — it also forces every deserializable type to be *statically* named at build time, which structurally prevents "parse whatever type the payload says."
- Content-length / max-body-size enforcement happens at the Kestrel / middleware layer *and* in the reader. Don't rely on just one — a middleware misconfiguration should still be caught by the parser.

## Tests to write
- Happy-path: valid JSON parses into a `CreateOrderRequest` with expected values.
- Missing required field: `{"customerId":"c1"}` throws `JsonException` (the `required` modifier enforces this).
- Over-size payload: 65 KB of valid JSON throws `PayloadTooLargeException` *before* parsing.
- Over-deep payload: 20 levels of nested arrays throws — regression for `MaxDepth`.
- Out-of-range field: `TotalCents = -1` throws `ValidationException` *after* parse.
- Type-confusion attempt: a payload containing `$type` is rejected or ignored (System.Text.Json ignores unknown keys by default; verify with a test).
- `BinaryFormatter` regression: a repo-wide test that greps the compiled assembly for references to `BinaryFormatter` and fails if found.
- No fallback parser: feed malformed JSON and assert the reader does *not* try YAML / XML / anything else — just throws.
