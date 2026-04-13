# VibeGuard Search Improvements — Design Spec

**Date:** 2026-04-13
**Status:** Approved

## Problem Statement

Broad multi-topic prep queries miss relevant archetypes because:

1. Hyphenated compound keywords (`backup-codes`, `openid-connect`) never match tokenized intent fragments
2. All keyword matches score equally (+1.0) regardless of uniqueness
3. The keyword score divisor scales with intent length, punishing broad queries
4. all-MiniLM-L6-v2 is a general-purpose embedding model, not retrieval-optimized
5. MaxResults = 8 is too tight for multi-topic intents against a 60-archetype corpus

## Change 1: Compound Keyword Decomposition

**File:** `KeywordArchetypeIndex.cs` — `BuildKeywordIndex`

When indexing a hyphenated keyword like `backup-codes`, also index its sub-parts (`backup`, `codes`) as secondary entries. Sub-parts must be >= 3 characters and not in the stopword set.

The index value type changes from `FrozenSet<string>` (set of archetype IDs) to a structure that carries match weights:

- Exact keyword match: weight **1.0**
- Sub-part match: weight **0.5**

`AccumulateKeywordScores` reads the weight from the index instead of adding a flat 1.0.

## Change 2: IDF Weighting

**File:** `KeywordArchetypeIndex.cs`

Compute inverse document frequency at build time:

```
idf(term) = log((N + 1) / (df + 1)) + 1.0
```

Where N = total archetypes (60), df = number of archetypes containing that keyword. The +1 smoothing prevents division by zero and the +1.0 floor ensures even common terms contribute.

Score range: "oauth" (df=1) -> idf ~ 4.4. "token" (df=5) -> idf ~ 3.4.

`AccumulateKeywordScores` becomes: `score += matchWeight * idf(term)`

## Change 3: Intent-Length-Independent Normalization

**File:** `KeywordArchetypeIndex.cs` — `SearchAsync`

Replace `var divisor = tokens.Count * 1.5` with a fixed corpus-derived constant:

```
divisor = 2.0 * log(corpusSize + 1)
```

Approximately 8.2 for 60 archetypes. Score no longer depends on intent length — only actual keyword matches matter.

Score examples:
- Single unique keyword (idf ~ 4.4) + 2 title hits (1.0) -> 5.4 / 8.2 = 0.66
- Two unique keywords -> saturates at 1.0
- Single common sub-part (idf ~ 2.4, weight 0.5) + 1 title hit -> 1.7 / 8.2 = 0.21

## Change 4: Model Swap to bge-small-en-v1.5

**Files:** `OnnxEmbeddingGenerator.cs`, `VibeGuard.Content.csproj`, embedded resources

bge-small-en-v1.5 is a retrieval-optimized model. Same 384 dimensions, BERT tokenizer.

Differences from MiniLM:
- MTEB Retrieval score: ~51 vs ~41 (25% better)
- Pooling: CLS token (first token) instead of mean-pool
- Query instruction prefix: prepend `"Represent this sentence for searching relevant passages: "` to query intents only (not archetype documents)
- Different vocab.txt (same 30,522 token count)

Changes needed:
- Replace `Models/all-MiniLM-L6-v2.onnx` with bge-small-en-v1.5 ONNX export
- Replace `Models/vocab.txt` with bge vocabulary
- Update resource name constants in `OnnxEmbeddingGenerator`
- Switch `MeanPool` to `ClsPool` (simpler: read first token's embedding)
- In `HybridSearchService.SearchAsync`, prepend query prefix to intent before embedding

`EmbeddingDimension` stays 384. `MaxSequenceLength` stays 256. Index structures unchanged.

## Change 5: MaxResults 8 -> 15

**File:** `PrepService.cs`

Bump from 8 to 15. With 60 archetypes, 15 results is 25% of corpus. For multi-topic intents used by teams, this ensures breadth without overwhelming noise.

## Files Changed

| File | Change |
|---|---|
| `KeywordArchetypeIndex.cs` | Compound decomposition, IDF weighting, new normalization |
| `HybridSearchService.cs` | Query prefix for bge model |
| `OnnxEmbeddingGenerator.cs` | Resource names, CLS pooling |
| `VibeGuard.Content.csproj` | New embedded resource names |
| `Models/` | Swap ONNX model + vocab |
| `PrepService.cs` | MaxResults 8 -> 15 |
| Tests | Update thresholds, add keyword/IDF/normalization tests |

## Testing

- All existing tests pass (update semantic similarity thresholds for new model)
- Compound keyword "backup codes" matches auth/mfa via sub-part decomposition
- IDF gives "oauth" (unique) higher score than "token" (common)
- Same intent at different lengths produces identical keyword score for same archetype
- Integration: auth prep query returns MFA, JWT, and OAuth within top 15
- Full `dotnet test` green before any commit
