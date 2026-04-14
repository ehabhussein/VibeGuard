# Semantic Search for `prep()` — Design Spec

> **Status:** Approved  
> **Date:** 2026-04-12  
> **Goal:** Add embedding-based semantic search to `prep()` so intent queries find relevant archetypes even when the user's phrasing doesn't match frontmatter keywords.

## Problem

`prep()` today uses keyword matching only (`KeywordArchetypeIndex`). A query like "prevent SQL attacks" won't find the `injection/sql-injection` archetype unless "SQL" or "attacks" appears in its `keywords` frontmatter. Users have to guess the right terminology. Semantic search closes that gap by matching on meaning, not tokens.

## Architecture

The embedding pipeline mirrors the existing keyword index: **computed once at startup, immutable at runtime, no network I/O on the hot path.**

### Invariants (non-negotiable)

1. **Deterministic** — Same corpus in, same embeddings out, same search results. The ONNX model is a pure function.
2. **Self-contained** — The model ships as an embedded resource in the assembly. No downloads, no network, no setup commands.
3. **No runtime mutation** — Embedding vectors are stored in a `FrozenDictionary<string, float[]>` built once at startup, just like the keyword index.
4. **Hot path is a pure read** — `prep()` embeds the query (one ONNX inference call), computes cosine similarity against all archetype vectors (microseconds for a 37-100 element corpus), blends with keyword score, returns ranked results. No allocations beyond the result list.

### Scoring

Hybrid weighted blend:

```
final_score = (0.3 * keyword_score) + (0.7 * semantic_score)
```

- **keyword_score**: Existing `KeywordArchetypeIndex` normalized score (0.0–1.0).
- **semantic_score**: Cosine similarity between query embedding and archetype embedding (0.0–1.0 after clamping; embeddings are L2-normalized so dot product = cosine similarity).
- Weights are compile-time constants. No configuration surface until proven necessary.
- Results sorted descending by `final_score`, tie-broken by archetype ID (lexicographic, stable).

### Model

- **all-MiniLM-L6-v2** (ONNX format)
- 384-dimensional output vectors
- ~22 MB on disk
- Shipped as an embedded resource in `VibeGuard.Content.dll`
- Tokenizer: `Microsoft.ML.Tokenizers.BertTokenizer` with the model's `vocab.txt`. The standard HuggingFace ONNX export does not embed the tokenizer graph, so a separate tokenizer is required. The vocabulary file (~230 KB) ships as a second embedded resource.

### What gets embedded

For each archetype, the searchable text is the concatenation of:

```
{title}\n{summary}\n{keywords joined by space}
```

This captures the archetype's identity without diluting the signal with full markdown body text. The same concatenation logic is used consistently for both index construction and (if needed in the future) re-indexing.

For the query, the raw `intent` string passed to `prep()` is embedded as-is. No preprocessing beyond what the tokenizer does internally.

## Components

### New files

#### 1. `src/VibeGuard.Content/Indexing/EmbeddingArchetypeIndex.cs`

Builds the embedding index at startup. Responsibilities:
- Takes `IReadOnlyList<Archetype>` + `IEmbeddingGenerator<string, Embedding<float>>` in constructor
- Embeds each archetype's searchable text (title + summary + keywords)
- Stores results in `FrozenDictionary<string, float[]>` (archetype ID → L2-normalized 384-dim vector)
- Exposes `Search(float[] queryEmbedding, int maxResults)` returning `IReadOnlyList<(string ArchetypeId, double Score)>`
- Cosine similarity implemented as dot product (vectors are pre-normalized)

```csharp
public sealed class EmbeddingArchetypeIndex
{
    private readonly FrozenDictionary<string, float[]> _embeddings;

    // Build from archetype list + embedding generator
    public static async Task<EmbeddingArchetypeIndex> BuildAsync(
        IReadOnlyList<Archetype> archetypes,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        CancellationToken ct = default);

    // Search by pre-computed query vector
    public IReadOnlyList<(string ArchetypeId, double Score)> Search(
        ReadOnlySpan<float> queryEmbedding, int maxResults);
}
```

#### 2. `src/VibeGuard.Content/Indexing/OnnxEmbeddingGenerator.cs`

Implements `IEmbeddingGenerator<string, Embedding<float>>` from `Microsoft.Extensions.AI`. Responsibilities:
- Loads the ONNX model from an embedded resource stream on construction
- Loads `vocab.txt` from a second embedded resource for the `BertTokenizer`
- Tokenizes input text using `Microsoft.ML.Tokenizers.BertTokenizer`
- Runs inference via `Microsoft.ML.OnnxRuntime`
- Returns L2-normalized 384-dim `Embedding<float>`
- Implements `IDisposable` (holds the `InferenceSession`)
- **Sync/async boundary**: ONNX inference is CPU-bound with no I/O. `GenerateAsync` performs synchronous inference and returns `Task.FromResult`. This means `HybridSearchService.Search` can call `.GetAwaiter().GetResult()` on the hot path without deadlock risk — there is no actual async operation to block on.

```csharp
public sealed class OnnxEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>, IDisposable
{
    private readonly InferenceSession _session;

    public OnnxEmbeddingGenerator(Stream modelStream);

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken ct = default);

    public void Dispose();
}
```

#### 3. `src/VibeGuard.Content/Indexing/HybridSearchService.cs`

Combines keyword and semantic search. Responsibilities:
- Takes `KeywordArchetypeIndex`, `EmbeddingArchetypeIndex`, and `IEmbeddingGenerator` via constructor
- `Search(intent, language, maxResults)`:
  1. Embeds the query via `IEmbeddingGenerator`
  2. Gets keyword results from `KeywordArchetypeIndex.Search()`
  3. Gets semantic results from `EmbeddingArchetypeIndex.Search()`
  4. Blends scores: `0.3 * keyword + 0.7 * semantic`
  5. Filters by language (same `AppliesToLanguage` logic as keyword index)
  6. Returns `IReadOnlyList<PrepMatch>` sorted by blended score

```csharp
public sealed class HybridSearchService : IArchetypeIndex
{
    private const double KeywordWeight = 0.3;
    private const double SemanticWeight = 0.7;

    public HybridSearchService(
        KeywordArchetypeIndex keywordIndex,
        EmbeddingArchetypeIndex embeddingIndex,
        IEmbeddingGenerator<string, Embedding<float>> generator);

    // IArchetypeIndex implementation — delegates GetById/GetReverseRelated
    // to keywordIndex, Search uses hybrid blend
}
```

### Modified files

#### 4. `src/VibeGuard.Content/VibeGuard.Content.csproj`

Add package references (no version — Central Package Management):
```xml
<PackageReference Include="Microsoft.ML.OnnxRuntime" />
<PackageReference Include="Microsoft.Extensions.AI.Abstractions" />
<PackageReference Include="Microsoft.ML.Tokenizers" />
```

Add embedded resources for the ONNX model and vocabulary:
```xml
<ItemGroup>
  <EmbeddedResource Include="Models\all-MiniLM-L6-v2.onnx" LogicalName="VibeGuard.Content.Models.all-MiniLM-L6-v2.onnx" />
  <EmbeddedResource Include="Models\vocab.txt" LogicalName="VibeGuard.Content.Models.vocab.txt" />
</ItemGroup>
```

#### 5. `Directory.Packages.props`

Add version entries:
```xml
<PackageVersion Include="Microsoft.ML.OnnxRuntime" Version="{latest}" />
<PackageVersion Include="Microsoft.Extensions.AI.Abstractions" Version="{latest}" />
<PackageVersion Include="Microsoft.ML.Tokenizers" Version="{latest}" />
```

#### 6. `src/VibeGuard.Mcp/Program.cs`

Update `RegisterVibeGuardServices` to wire up the embedding pipeline:
- Register `OnnxEmbeddingGenerator` as `IEmbeddingGenerator<string, Embedding<float>>` (singleton, disposable)
- Build `EmbeddingArchetypeIndex` at startup alongside the keyword index
- Register `HybridSearchService` as `IArchetypeIndex` (replacing the current `KeywordArchetypeIndex` registration)
- `KeywordArchetypeIndex` still gets built (used internally by `HybridSearchService`) but is no longer the registered `IArchetypeIndex`

#### 7. `src/VibeGuard.Content/Services/PrepService.cs`

No changes needed. `PrepService` depends on `IArchetypeIndex`, which will now resolve to `HybridSearchService` instead of `KeywordArchetypeIndex`. The interface is unchanged.

### Model file

#### 8. `src/VibeGuard.Content/Models/all-MiniLM-L6-v2.onnx`

The ONNX model binary (~22 MB) and its vocabulary file (`vocab.txt`, ~230 KB). Both checked into the repo and embedded in the assembly at build time. The model is Apache-2.0 licensed (sentence-transformers/all-MiniLM-L6-v2).

## Data flow

```
prep("prevent injection attacks")
  │
  ├──► KeywordArchetypeIndex.Search("prevent injection attacks", "csharp", 8)
  │      → keyword_results: [{id: "injection/sql-injection", score: 0.6}, ...]
  │
  ├──► OnnxEmbeddingGenerator.GenerateAsync("prevent injection attacks")
  │      → float[384] query_vector
  │
  ├──► EmbeddingArchetypeIndex.Search(query_vector, 8)
  │      → semantic_results: [{id: "injection/sql-injection", score: 0.92}, ...]
  │
  └──► HybridSearchService blends:
         injection/sql-injection: 0.3 * 0.6 + 0.7 * 0.92 = 0.824
         → sorted, language-filtered, returned as IReadOnlyList<PrepMatch>
```

## What doesn't change

- **`consult()`** — Unchanged. Works by archetype ID, not search.
- **`KeywordArchetypeIndex`** — Stays. Still built at startup. Used by `HybridSearchService` for the keyword component.
- **`FileSystemArchetypeRepository`** — Unchanged. Still loads archetypes from disk.
- **All YAML frontmatter, markdown content, validation** — Untouched.
- **Transport layer (stdio/HTTP)** — Untouched.
- **`IArchetypeIndex` interface** — Unchanged. `HybridSearchService` implements it.
- **`PrepMatch` record** — Unchanged. Score semantics shift (now blended) but the type is the same.

## Testing strategy

### Unit tests (no ONNX model)

- `EmbeddingArchetypeIndex` tested with mock `IEmbeddingGenerator` that returns known vectors. Verifies cosine similarity ranking, score normalization, max-results capping.
- `HybridSearchService` tested with mock keyword index and mock embedding index. Verifies blend weights, deduplication, language filtering, tie-breaking.

### Integration tests (with ONNX model)

- Load real model, embed "password hashing" and verify the auth archetype ranks higher than with keyword-only search.
- Load real model, embed "prevent SQL attacks" and verify `injection/sql-injection` is in the top 3.
- Startup smoke test: verify the full DI container wires up and the first `prep()` call completes without errors.

### Existing tests

- All existing `PrepService` and `KeywordArchetypeIndex` tests continue to pass. `PrepService` tests exercise the `IArchetypeIndex` interface, which `HybridSearchService` now implements. `KeywordArchetypeIndex` tests are unit tests for that specific class and are unaffected.

## Performance budget

- **Startup**: Model load + embed 37 archetypes ≈ 2-5 seconds (acceptable, one-time cost, happens before first request).
- **Per-query**: One ONNX inference (~5-15ms for a short query on CPU) + one cosine scan over 37-100 vectors (microseconds). Total per-query overhead: ~5-15ms. Acceptable for an MCP tool call.
- **Memory**: ONNX session ~50MB resident + 37 * 384 * 4 bytes = ~57KB for embedding vectors. Total additional memory: ~50MB.
- **Binary size**: +22MB for the ONNX model. Total binary goes from ~15MB to ~37MB per platform.

## Risks and mitigations

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| ONNX model too large for git | Low | 22MB is within GitHub's 100MB file limit. If it grows, use Git LFS. |
| Startup time regression | Medium | Log embedding index build time. If >5s, consider lazy initialization (build on first `prep()` call). |
| OnnxRuntime native binary per platform | Medium | `Microsoft.ML.OnnxRuntime` ships platform-specific NuGet packages. The RID-specific publish already handles this. Verify all 6 platform builds. |
| Tokenizer mismatch | Low | Use the exact tokenizer the model was trained with. all-MiniLM-L6-v2 uses a standard BERT WordPiece tokenizer available in `Microsoft.ML.Tokenizers`. |
| Score meaning shift | Low | `PrepMatch.Score` changes from keyword-only to blended. No external contract depends on score magnitude — scores are relative per-query only. |

## Package versions

All packages use latest stable per project policy. Specific versions resolved at implementation time and pinned in `Directory.Packages.props`.

Required new packages:
- `Microsoft.ML.OnnxRuntime` — ONNX inference engine
- `Microsoft.Extensions.AI.Abstractions` — `IEmbeddingGenerator` interface
- `Microsoft.ML.Tokenizers` — BERT WordPiece tokenizer for input preprocessing
