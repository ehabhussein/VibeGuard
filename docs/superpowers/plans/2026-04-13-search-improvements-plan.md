# Search Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve VibeGuard prep search so multi-topic intents reliably surface all relevant archetypes — fix compound keyword matching, add IDF weighting, swap to a retrieval-optimized embedding model, and bump result limit to 15.

**Architecture:** Three-layer improvement: keyword scoring (compound decomposition + IDF + intent-length-independent normalization), semantic scoring (bge-small-en-v1.5 with CLS pooling and query prefix), and result cap (8 -> 15). All changes are in VibeGuard.Content and VibeGuard.Mcp.

**Tech Stack:** C# 14 / .NET 10, ONNX Runtime, Microsoft.ML.Tokenizers, bge-small-en-v1.5

**Note:** No commits until the full test suite passes.

---

### Task 1: Download and place bge-small-en-v1.5 model files

**Files:**
- Replace: `src/VibeGuard.Content/Models/all-MiniLM-L6-v2.onnx`
- Replace: `src/VibeGuard.Content/Models/vocab.txt`

- [ ] **Step 1: Export bge-small-en-v1.5 to ONNX**

```bash
pip install optimum[onnxruntime]
optimum-cli export onnx --model BAAI/bge-small-en-v1.5 --task feature-extraction ./bge-export
```

Expected output: `bge-export/model.onnx`, `bge-export/vocab.txt`, and other tokenizer files.

- [ ] **Step 2: Replace model files**

```bash
cp ./bge-export/model.onnx src/VibeGuard.Content/Models/bge-small-en-v1.5.onnx
cp ./bge-export/vocab.txt src/VibeGuard.Content/Models/bge-vocab.txt
```

Keep the old files until the swap is verified, then delete them.

- [ ] **Step 3: Update csproj embedded resource declarations**

In `src/VibeGuard.Content/VibeGuard.Content.csproj`, replace the embedded resource block:

```xml
<EmbeddedResource Include="Models\bge-small-en-v1.5.onnx" LogicalName="VibeGuard.Content.Models.bge-small-en-v1.5.onnx" />
<EmbeddedResource Include="Models\bge-vocab.txt" LogicalName="VibeGuard.Content.Models.bge-vocab.txt" />
```

- [ ] **Step 4: Delete old model files**

```bash
rm src/VibeGuard.Content/Models/all-MiniLM-L6-v2.onnx
rm src/VibeGuard.Content/Models/vocab.txt
```

---

### Task 2: Update OnnxEmbeddingGenerator for bge model

**Files:**
- Modify: `src/VibeGuard.Content/Indexing/OnnxEmbeddingGenerator.cs`

- [ ] **Step 1: Update resource name constants**

```csharp
private const string ModelResourceName = "VibeGuard.Content.Models.bge-small-en-v1.5.onnx";
private const string VocabResourceName = "VibeGuard.Content.Models.bge-vocab.txt";
```

`EmbeddingDimension` stays 384. `MaxSequenceLength` stays 256.

- [ ] **Step 2: Replace MeanPool with ClsPool**

Replace the `MeanPool` method (lines 105-121) with:

```csharp
private static float[] ClsPool(Microsoft.ML.OnnxRuntime.Tensors.Tensor<float> tensor)
{
    var result = new float[EmbeddingDimension];
    for (var d = 0; d < EmbeddingDimension; d++)
        result[d] = tensor[0, 0, d];
    return result;
}
```

- [ ] **Step 3: Update Embed method to use ClsPool**

In the `Embed` method, replace:
```csharp
var pooled = MeanPool(outputTensor, seqLen);
```
with:
```csharp
var pooled = ClsPool(outputTensor);
```

The `seqLen` parameter is no longer needed by the pooling method.

---

### Task 3: Add compound keyword decomposition and IDF to KeywordArchetypeIndex

**Files:**
- Modify: `src/VibeGuard.Content/Indexing/KeywordArchetypeIndex.cs`

This is the largest change. The keyword index needs three modifications: compound decomposition, IDF weight storage, and intent-length-independent normalization.

- [ ] **Step 1: Change the keyword index value type**

Replace the field declaration:
```csharp
private readonly FrozenDictionary<string, FrozenSet<string>> _keywordIndex;
```
with:
```csharp
private readonly FrozenDictionary<string, FrozenDictionary<string, double>> _keywordIndex;
private readonly FrozenDictionary<string, double> _idfWeights;
```

Update the constructor to accept and store both:
```csharp
private KeywordArchetypeIndex(
    FrozenDictionary<string, Archetype> byId,
    FrozenDictionary<string, FrozenDictionary<string, double>> keywordIndex,
    FrozenDictionary<string, double> idfWeights,
    FrozenDictionary<string, ImmutableArray<string>> reverseRelated)
{
    _byId = byId;
    _keywordIndex = keywordIndex;
    _idfWeights = idfWeights;
    _reverseRelated = reverseRelated;
}
```

- [ ] **Step 2: Rewrite BuildKeywordIndex to decompose compounds and store weights**

Replace the `BuildKeywordIndex` method entirely:

```csharp
private static (
    FrozenDictionary<string, FrozenDictionary<string, double>> Index,
    FrozenDictionary<string, double> Idf
) BuildKeywordIndex(IReadOnlyList<Archetype> archetypes)
{
    // Phase 1: collect (term -> archetype -> weight) with compound decomposition.
    var accumulator = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

    void AddEntry(string term, string archetypeId, double weight)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Length < 2) return;
        if (Stopwords.Contains(term)) return;
        if (!accumulator.TryGetValue(term, out var bucket))
        {
            bucket = new Dictionary<string, double>(StringComparer.Ordinal);
            accumulator[term] = bucket;
        }
        // Keep the highest weight if the same archetype appears under multiple paths.
        bucket.TryGetValue(archetypeId, out var existing);
        bucket[archetypeId] = Math.Max(existing, weight);
    }

    foreach (var archetype in archetypes)
    {
        foreach (var keyword in archetype.Principles.Keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword)) continue;
            // Exact keyword: weight 1.0
            AddEntry(keyword, archetype.Id, 1.0);
            // Compound decomposition: split on hyphens, index sub-parts at 0.5
            if (keyword.Contains('-'))
            {
                foreach (var part in keyword.Split('-'))
                {
                    if (part.Length >= 3)
                        AddEntry(part, archetype.Id, 0.5);
                }
            }
        }
    }

    // Phase 2: freeze the index.
    var frozen = new Dictionary<string, FrozenDictionary<string, double>>(
        accumulator.Count, StringComparer.OrdinalIgnoreCase);
    foreach (var (term, bucket) in accumulator)
        frozen[term] = bucket.ToFrozenDictionary(StringComparer.Ordinal);

    var index = frozen.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // Phase 3: compute IDF for each term.
    var n = archetypes.Count;
    var idf = new Dictionary<string, double>(accumulator.Count, StringComparer.OrdinalIgnoreCase);
    foreach (var (term, bucket) in accumulator)
    {
        var df = bucket.Count; // number of distinct archetypes this term maps to
        idf[term] = Math.Log((n + 1.0) / (df + 1.0)) + 1.0;
    }

    return (index, idf.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
}
```

- [ ] **Step 3: Update the Build factory to wire up the new index**

```csharp
public static KeywordArchetypeIndex Build(IReadOnlyList<Archetype> archetypes)
{
    ArgumentNullException.ThrowIfNull(archetypes);
    var byId = BuildByIdIndex(archetypes);
    var (keywordIndex, idfWeights) = BuildKeywordIndex(archetypes);
    var reverseRelated = BuildReverseRelatedIndex(archetypes);
    return new KeywordArchetypeIndex(byId, keywordIndex, idfWeights, reverseRelated);
}
```

- [ ] **Step 4: Rewrite AccumulateKeywordScores to use IDF and weights**

```csharp
private void AccumulateKeywordScores(IReadOnlyList<string> tokens, Dictionary<string, double> scores)
{
    foreach (var token in tokens)
    {
        if (!_keywordIndex.TryGetValue(token, out var archetypeWeights)) continue;
        var idf = _idfWeights[token];
        foreach (var (id, matchWeight) in archetypeWeights)
        {
            scores.TryGetValue(id, out var current);
            scores[id] = current + (matchWeight * idf);
        }
    }
}
```

- [ ] **Step 5: Replace the intent-length-dependent normalization**

In `SearchAsync`, replace:
```csharp
var divisor = tokens.Count * 1.5;
```
with:
```csharp
var divisor = 2.0 * Math.Log(_byId.Count + 1);
```

The rest of the normalization logic (`Math.Min(1.0, raw / divisor)`) stays the same.

---

### Task 4: Add query instruction prefix in HybridSearchService

**Files:**
- Modify: `src/VibeGuard.Content/Indexing/HybridSearchService.cs`

- [ ] **Step 1: Add the bge query prefix constant**

At the top of the class, add:
```csharp
private const string QueryPrefix = "Represent this sentence for searching relevant passages: ";
```

- [ ] **Step 2: Prepend the prefix when embedding the query**

In `SearchAsync`, change:
```csharp
var embeddingResult = await _generator.GenerateAsync([intent], cancellationToken: ct).ConfigureAwait(false);
```
to:
```csharp
var embeddingResult = await _generator.GenerateAsync([QueryPrefix + intent], cancellationToken: ct).ConfigureAwait(false);
```

This only affects the query embedding. Archetype documents are embedded without the prefix during `EmbeddingArchetypeIndex.BuildAsync` — that path is unchanged.

---

### Task 5: Bump MaxResults to 15

**Files:**
- Modify: `src/VibeGuard.Content/Services/PrepService.cs`
- Modify: `src/VibeGuard.Mcp/Tools/PrepTool.cs`

- [ ] **Step 1: Update PrepService constant and comment**

In `PrepService.cs`, change:
```csharp
/// caps result count per spec §3.1 (max 8 matches).
```
to:
```csharp
/// caps result count (max 15 matches).
```

And change:
```csharp
public const int MaxResults = 8;
```
to:
```csharp
public const int MaxResults = 15;
```

- [ ] **Step 2: Update PrepTool description**

In `PrepTool.cs`, change:
```csharp
"and receive up to 8 ranked archetype identifiers to consult().")]
```
to:
```csharp
"and receive up to 15 ranked archetype identifiers to consult().")]
```

---

### Task 6: Update tests for keyword scoring changes

**Files:**
- Modify: `tests/VibeGuard.Content.Tests/KeywordArchetypeIndexTests.cs`

Existing tests should still pass because:
- `Search_ByKeyword_ReturnsHit`: "password" still matches — IDF changes the score but the hit still surfaces
- `Search_LanguageNotInAppliesTo_FiltersOut`: language filtering is unchanged
- `Search_MaxResults_IsRespected`: max results is a parameter, not a constant
- `GetReverseRelated_*` and `GetById_*`: unrelated to scoring

- [ ] **Step 1: Add test for compound keyword matching**

Append to `KeywordArchetypeIndexTests`:

```csharp
[Fact]
public async Task Search_CompoundKeywordSubParts_MatchViaParts()
{
    var mfa = MakeArchetype(
        "auth/mfa",
        "Multi-Factor Authentication",
        "Implementing TOTP, WebAuthn, and backup codes.",
        new[] { "mfa", "totp", "backup-codes" },
        new[] { "all" });
    var index = KeywordArchetypeIndex.Build(new[] { mfa });

    var ct = TestContext.Current.CancellationToken;
    // "backup" and "codes" are sub-parts of the compound keyword "backup-codes"
    var hits = await index.SearchAsync("implement backup codes", "csharp", maxResults: 8, ct);

    hits.Should().ContainSingle()
        .Which.ArchetypeId.Should().Be("auth/mfa");
}

[Fact]
public async Task Search_ExactCompoundKeyword_ScoresHigherThanSubPart()
{
    var mfa = MakeArchetype(
        "auth/mfa",
        "Multi-Factor Authentication",
        "MFA summary.",
        new[] { "backup-codes" },
        new[] { "all" });
    var index = KeywordArchetypeIndex.Build(new[] { mfa });

    var ct = TestContext.Current.CancellationToken;
    var exactHits = await index.SearchAsync("backup-codes", "csharp", maxResults: 8, ct);
    var subPartHits = await index.SearchAsync("backup", "csharp", maxResults: 8, ct);

    exactHits.Should().ContainSingle();
    subPartHits.Should().ContainSingle();
    exactHits[0].Score.Should().BeGreaterThan(subPartHits[0].Score);
}
```

- [ ] **Step 2: Add test for IDF weighting**

```csharp
[Fact]
public async Task Search_UniqueKeyword_ScoresHigherThanCommonKeyword()
{
    var oauth = MakeArchetype(
        "auth/oauth", "OAuth", "OAuth integration.",
        new[] { "oauth", "token" }, new[] { "all" });
    var session = MakeArchetype(
        "auth/session", "Session", "Session management.",
        new[] { "session", "token" }, new[] { "all" });
    var jwt = MakeArchetype(
        "auth/jwt", "JWT", "JWT handling.",
        new[] { "jwt", "token" }, new[] { "all" });
    var index = KeywordArchetypeIndex.Build(new[] { oauth, session, jwt });

    var ct = TestContext.Current.CancellationToken;
    // "oauth" appears in 1 archetype (high IDF), "token" appears in 3 (low IDF).
    // Searching for "oauth" should give auth/oauth a higher score than
    // searching for "token" gives any single archetype.
    var oauthHits = await index.SearchAsync("oauth", "csharp", maxResults: 8, ct);
    var tokenHits = await index.SearchAsync("token", "csharp", maxResults: 8, ct);

    oauthHits[0].Score.Should().BeGreaterThan(tokenHits[0].Score);
}
```

- [ ] **Step 3: Add test for intent-length-independent scoring**

```csharp
[Fact]
public async Task Search_LongIntent_SameScoreAsShortIntent()
{
    var oauth = MakeArchetype(
        "auth/oauth", "OAuth", "OAuth integration.",
        new[] { "oauth" }, new[] { "all" });
    var index = KeywordArchetypeIndex.Build(new[] { oauth });

    var ct = TestContext.Current.CancellationToken;
    var shortHits = await index.SearchAsync("implement oauth", "csharp", maxResults: 8, ct);
    var longHits = await index.SearchAsync(
        "implement oauth along with password hashing session management rate limiting logging and error handling",
        "csharp", maxResults: 8, ct);

    shortHits.Should().ContainSingle();
    longHits.Should().ContainSingle();
    // Scores should be identical — the keyword match is the same ("oauth")
    // and the normalization no longer depends on intent length.
    longHits[0].Score.Should().BeApproximately(shortHits[0].Score, 0.01);
}
```

---

### Task 7: Update tests for model swap

**Files:**
- Modify: `tests/VibeGuard.Content.Tests/OnnxEmbeddingGeneratorTests.cs`
- Modify: `tests/VibeGuard.Content.Tests/HybridSearchIntegrationTests.cs`

- [ ] **Step 1: Verify OnnxEmbeddingGenerator tests**

The existing tests in `OnnxEmbeddingGeneratorTests.cs` should still pass because:
- `GenerateAsync_SingleInput_Returns384DimVector`: bge-small is also 384-dim
- `GenerateAsync_OutputIsL2Normalized`: we still L2-normalize
- `GenerateAsync_MultipleInputs_ReturnsSameCount`: batch behavior unchanged

The similarity thresholds may need adjustment:
- `GenerateAsync_SimilarInputs_HaveHighCosineSimilarity`: threshold `> 0.8` — may need lowering to `> 0.7` depending on bge's behavior for those specific inputs
- `GenerateAsync_DissimilarInputs_HaveLowCosineSimilarity`: threshold `< 0.5` — should still pass

Run:
```bash
dotnet test tests/VibeGuard.Content.Tests --filter "FullyQualifiedName~OnnxEmbeddingGeneratorTests" -v normal
```

If similarity thresholds fail, adjust them based on the actual values reported.

- [ ] **Step 2: Update HybridSearchIntegrationTests maxResults**

In `HybridSearchIntegrationTests.cs`, the tests use `maxResults: 8`. These should continue to work but can be updated to `maxResults: 15` for consistency with the new limit:

In all three test methods (`SemanticSearch_PasswordQuery_FindsAuthArchetype`, `SemanticSearch_InjectionQuery_FindsInjectionArchetype`, `SemanticSearch_VagueQuery_StillReturnsResults`), change:
```csharp
maxResults: 8
```
to:
```csharp
maxResults: 15
```

- [ ] **Step 3: Add integration test for multi-topic auth query**

Append to `HybridSearchIntegrationTests.cs`:

```csharp
[Fact]
public async Task SemanticSearch_BroadAuthQuery_SurfacesMfaJwtOauth()
{
    var ct = TestContext.Current.CancellationToken;
    var root = FindArchetypesRoot();
    var repo = new FileSystemArchetypeRepository(root, false, SupportedLanguageSet.Default());
    var archetypes = repo.LoadAll();

    using var generator = OnnxEmbeddingGenerator.Create();
    var keywordIndex = KeywordArchetypeIndex.Build(archetypes);
    var embeddingIndex = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator, ct);
    var hybrid = new HybridSearchService(keywordIndex, embeddingIndex, generator);

    var results = await hybrid.SearchAsync(
        "Build a secure authentication system including user registration, login, " +
        "password hashing, session management, MFA, JWT tokens, and OAuth integration",
        "csharp", maxResults: 15, ct);

    var ids = results.Select(r => r.ArchetypeId).ToList();
    ids.Should().Contain("auth/mfa");
    ids.Should().Contain("auth/jwt-handling");
    ids.Should().Contain("auth/oauth-integration");
    ids.Should().Contain("auth/password-hashing");
    ids.Should().Contain("auth/session-tokens");
}
```

---

### Task 8: Run full test suite

- [ ] **Step 1: Build the solution**

```bash
dotnet build
```

Expected: Build succeeded. 0 Errors.

- [ ] **Step 2: Run all tests**

```bash
dotnet test --verbosity normal
```

Expected: All tests pass. If any similarity threshold tests fail in `OnnxEmbeddingGeneratorTests`, adjust the thresholds based on the actual similarity values produced by the bge model (the tests will print the actual value in the failure message).

- [ ] **Step 3: Run the prep query manually to verify**

Start the MCP server and call prep with the auth query to verify MFA, JWT, and OAuth all surface in the top 15.
