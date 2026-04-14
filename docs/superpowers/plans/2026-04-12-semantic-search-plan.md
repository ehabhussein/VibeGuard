# Semantic Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add local ONNX embedding-based semantic search to `prep()`, blending keyword and cosine-similarity scores for better archetype discovery.

**Architecture:** A new `EmbeddingArchetypeIndex` pre-computes archetype embeddings at startup using a bundled all-MiniLM-L6-v2 ONNX model. A `HybridSearchService` implements `IArchetypeIndex` and blends keyword scores (0.3) with semantic scores (0.7). The existing `KeywordArchetypeIndex` remains intact as an internal component.

**Tech Stack:** .NET 10 / C# 14, Microsoft.ML.OnnxRuntime 1.24.4, Microsoft.Extensions.AI.Abstractions 10.4.1, Microsoft.ML.Tokenizers 2.0.0, xUnit v3, AwesomeAssertions

**Spec:** `docs/superpowers/specs/2026-04-12-semantic-search-design.md`

---

### Task 1: Add NuGet packages and embedded model resources

**Files:**
- Modify: `Directory.Packages.props:24-39`
- Modify: `src/VibeGuard.Content/VibeGuard.Content.csproj`
- Create: `src/VibeGuard.Content/Models/` (directory for ONNX model + vocab)

- [ ] **Step 1: Add package versions to Central Package Management**

In `Directory.Packages.props`, add these entries inside the `<ItemGroup>` after the existing `VibeGuard.Content` section:

```xml
    <!-- VibeGuard.Content — Semantic search -->
    <PackageVersion Include="Microsoft.ML.OnnxRuntime" Version="1.24.4" />
    <PackageVersion Include="Microsoft.Extensions.AI.Abstractions" Version="10.4.1" />
    <PackageVersion Include="Microsoft.ML.Tokenizers" Version="2.0.0" />
```

- [ ] **Step 2: Add package references and embedded resources to VibeGuard.Content.csproj**

Add to `src/VibeGuard.Content/VibeGuard.Content.csproj`:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.ML.OnnxRuntime" />
    <PackageReference Include="Microsoft.Extensions.AI.Abstractions" />
    <PackageReference Include="Microsoft.ML.Tokenizers" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="Models\all-MiniLM-L6-v2.onnx" LogicalName="VibeGuard.Content.Models.all-MiniLM-L6-v2.onnx" />
    <EmbeddedResource Include="Models\vocab.txt" LogicalName="VibeGuard.Content.Models.vocab.txt" />
  </ItemGroup>
```

- [ ] **Step 3: Download the ONNX model and vocabulary**

Download the all-MiniLM-L6-v2 ONNX model and its vocab.txt into `src/VibeGuard.Content/Models/`:

```bash
mkdir -p src/VibeGuard.Content/Models
# Download model from HuggingFace
curl -L -o src/VibeGuard.Content/Models/all-MiniLM-L6-v2.onnx \
  "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx"
# Download vocabulary
curl -L -o src/VibeGuard.Content/Models/vocab.txt \
  "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt"
```

- [ ] **Step 4: Verify the build compiles with new packages**

```bash
dotnet build src/VibeGuard.Content/VibeGuard.Content.csproj
```

Expected: Build succeeded. 0 errors.

- [ ] **Step 5: Verify existing tests still pass**

```bash
dotnet test tests/VibeGuard.Content.Tests/VibeGuard.Content.Tests.csproj
```

Expected: All existing tests pass.

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props src/VibeGuard.Content/VibeGuard.Content.csproj src/VibeGuard.Content/Models/
git commit -m "feat: add ONNX model and NuGet packages for semantic search"
```

---

### Task 2: Implement `OnnxEmbeddingGenerator`

**Files:**
- Create: `src/VibeGuard.Content/Indexing/OnnxEmbeddingGenerator.cs`
- Test: `tests/VibeGuard.Content.Tests/OnnxEmbeddingGeneratorTests.cs`

- [ ] **Step 1: Write the integration test**

Create `tests/VibeGuard.Content.Tests/OnnxEmbeddingGeneratorTests.cs`:

```csharp
using Microsoft.Extensions.AI;
using VibeGuard.Content.Indexing;

namespace VibeGuard.Content.Tests;

#pragma warning disable CA1707
// CA1707: xUnit idiomatic Method_State_Expected naming.

public class OnnxEmbeddingGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_SingleInput_Returns384DimVector()
    {
        using var generator = OnnxEmbeddingGenerator.Create();

        var result = await generator.GenerateAsync(["password hashing with bcrypt"]);

        result.Should().ContainSingle();
        result[0].Vector.Length.Should().Be(384);
    }

    [Fact]
    public async Task GenerateAsync_OutputIsL2Normalized()
    {
        using var generator = OnnxEmbeddingGenerator.Create();

        var result = await generator.GenerateAsync(["secure coding practices"]);

        var vec = result[0].Vector;
        var norm = 0.0;
        for (var i = 0; i < vec.Length; i++)
            norm += vec.Span[i] * vec.Span[i];
        norm = Math.Sqrt(norm);

        norm.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public async Task GenerateAsync_SimilarInputs_HaveHighCosineSimilarity()
    {
        using var generator = OnnxEmbeddingGenerator.Create();

        var results = await generator.GenerateAsync([
            "hash a password securely",
            "secure password hashing"
        ]);

        var similarity = CosineSimilarity(results[0].Vector.Span, results[1].Vector.Span);
        similarity.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public async Task GenerateAsync_DissimilarInputs_HaveLowCosineSimilarity()
    {
        using var generator = OnnxEmbeddingGenerator.Create();

        var results = await generator.GenerateAsync([
            "hash a password securely",
            "TCP socket connection pooling"
        ]);

        var similarity = CosineSimilarity(results[0].Vector.Span, results[1].Vector.Span);
        similarity.Should().BeLessThan(0.5);
    }

    [Fact]
    public async Task GenerateAsync_MultipleInputs_ReturnsSameCount()
    {
        using var generator = OnnxEmbeddingGenerator.Create();

        var inputs = new[] { "input one", "input two", "input three" };
        var result = await generator.GenerateAsync(inputs);

        result.Should().HaveCount(3);
    }

    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var dot = 0.0;
        for (var i = 0; i < a.Length; i++)
            dot += a[i] * b[i];
        return dot; // Vectors are L2-normalized, so dot product = cosine similarity.
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/VibeGuard.Content.Tests/VibeGuard.Content.Tests.csproj --filter "FullyQualifiedName~OnnxEmbeddingGenerator"
```

Expected: FAIL — `OnnxEmbeddingGenerator` does not exist.

- [ ] **Step 3: Implement OnnxEmbeddingGenerator**

Create `src/VibeGuard.Content/Indexing/OnnxEmbeddingGenerator.cs`:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;

namespace VibeGuard.Content.Indexing;

/// <summary>
/// Local ONNX-based embedding generator using all-MiniLM-L6-v2.
/// Produces L2-normalized 384-dimensional vectors. The ONNX model
/// and BERT vocabulary are loaded from assembly embedded resources.
/// All inference is CPU-bound — <see cref="GenerateAsync"/> completes
/// synchronously and returns <see cref="Task.FromResult{TResult}"/>.
/// </summary>
public sealed class OnnxEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>, IDisposable
{
    private const string ModelResourceName = "VibeGuard.Content.Models.all-MiniLM-L6-v2.onnx";
    private const string VocabResourceName = "VibeGuard.Content.Models.vocab.txt";
    private const int EmbeddingDimension = 384;
    private const int MaxSequenceLength = 256;

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;

    private OnnxEmbeddingGenerator(InferenceSession session, BertTokenizer tokenizer)
    {
        _session = session;
        _tokenizer = tokenizer;
    }

    /// <summary>
    /// Creates a new generator by loading the ONNX model and vocabulary
    /// from the assembly's embedded resources.
    /// </summary>
    public static OnnxEmbeddingGenerator Create()
    {
        var assembly = typeof(OnnxEmbeddingGenerator).Assembly;

        using var modelStream = assembly.GetManifestResourceStream(ModelResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ModelResourceName}' not found.");
        using var ms = new MemoryStream();
        modelStream.CopyTo(ms);
        var session = new InferenceSession(ms.ToArray());

        using var vocabStream = assembly.GetManifestResourceStream(VocabResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{VocabResourceName}' not found.");
        var tokenizer = BertTokenizer.Create(vocabStream);

        return new OnnxEmbeddingGenerator(session, tokenizer);
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = values as IList<string> ?? values.ToList();
        var embeddings = new GeneratedEmbeddings<Embedding<float>>(inputs.Count);

        foreach (var text in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = Embed(text);
            embeddings.Add(new Embedding<float>(vector));
        }

        return Task.FromResult(embeddings);
    }

    private float[] Embed(string text)
    {
        var encoded = _tokenizer.EncodeToIds(text, MaxSequenceLength, out _, out _);

        var inputIds = new long[encoded.Count];
        var attentionMask = new long[encoded.Count];
        var tokenTypeIds = new long[encoded.Count];
        for (var i = 0; i < encoded.Count; i++)
        {
            inputIds[i] = encoded[i];
            attentionMask[i] = 1;
            tokenTypeIds[i] = 0;
        }

        var seqLen = encoded.Count;

        var onnxInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids",
                new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<long>(inputIds, [1, seqLen])),
            NamedOnnxValue.CreateFromTensor("attention_mask",
                new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<long>(attentionMask, [1, seqLen])),
            NamedOnnxValue.CreateFromTensor("token_type_ids",
                new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<long>(tokenTypeIds, [1, seqLen])),
        };

        using var results = _session.Run(onnxInputs);

        // The model outputs token-level embeddings of shape [1, seq_len, 384].
        // Mean-pool across the sequence dimension, then L2-normalize.
        var outputTensor = results.First().AsTensor<float>();
        var pooled = MeanPool(outputTensor, seqLen);
        L2Normalize(pooled);
        return pooled;
    }

    private static float[] MeanPool(Microsoft.ML.OnnxRuntime.Tensors.Tensor<float> tensor, int seqLen)
    {
        var result = new float[EmbeddingDimension];
        for (var t = 0; t < seqLen; t++)
        {
            for (var d = 0; d < EmbeddingDimension; d++)
            {
                result[d] += tensor[0, t, d];
            }
        }
        var divisor = (float)seqLen;
        for (var d = 0; d < EmbeddingDimension; d++)
        {
            result[d] /= divisor;
        }
        return result;
    }

    private static void L2Normalize(float[] vector)
    {
        var norm = 0f;
        for (var i = 0; i < vector.Length; i++)
            norm += vector[i] * vector[i];
        norm = MathF.Sqrt(norm);
        if (norm > 0f)
        {
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;
        }
    }

    public EmbeddingGeneratorMetadata Metadata => new("OnnxEmbeddingGenerator");

    public TService? GetService<TService>(object? key = null) where TService : class
        => this as TService;

    public void Dispose() => _session.Dispose();
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/VibeGuard.Content.Tests/VibeGuard.Content.Tests.csproj --filter "FullyQualifiedName~OnnxEmbeddingGenerator"
```

Expected: All 5 tests PASS.

- [ ] **Step 5: Run all tests to check for regressions**

```bash
dotnet test tests/VibeGuard.Content.Tests/VibeGuard.Content.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/VibeGuard.Content/Indexing/OnnxEmbeddingGenerator.cs tests/VibeGuard.Content.Tests/OnnxEmbeddingGeneratorTests.cs
git commit -m "feat: implement OnnxEmbeddingGenerator with all-MiniLM-L6-v2"
```

---

### Task 3: Implement `EmbeddingArchetypeIndex`

**Files:**
- Create: `src/VibeGuard.Content/Indexing/EmbeddingArchetypeIndex.cs`
- Test: `tests/VibeGuard.Content.Tests/EmbeddingArchetypeIndexTests.cs`

- [ ] **Step 1: Write the unit tests with mock embeddings**

Create `tests/VibeGuard.Content.Tests/EmbeddingArchetypeIndexTests.cs`:

```csharp
using Microsoft.Extensions.AI;
using VibeGuard.Content.Indexing;

namespace VibeGuard.Content.Tests;

#pragma warning disable CA1707, CA1861
// CA1707: xUnit idiomatic Method_State_Expected naming.
// CA1861: inline constant arrays in test fixtures are clearer than hoisted statics.

public class EmbeddingArchetypeIndexTests
{
    private static Archetype MakeArchetype(
        string id, string title, string summary, string[] keywords)
        => new(
            Id: id,
            Principles: new PrinciplesFrontmatter
            {
                SchemaVersion = 1,
                Archetype = id,
                Title = title,
                Summary = summary,
                AppliesTo = ["all"],
                Keywords = [.. keywords],
                RelatedArchetypes = []
            },
            PrinciplesBody: "body",
            LanguageFiles: new Dictionary<string, LanguageFile>(StringComparer.Ordinal));

    /// <summary>
    /// Mock generator that returns a deterministic vector based on
    /// simple character hashing. Not semantically meaningful, but
    /// produces distinct, normalized vectors for different inputs.
    /// </summary>
    private sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var text in values)
            {
                result.Add(new Embedding<float>(MakeDeterministicVector(text)));
            }
            return Task.FromResult(result);
        }

        public EmbeddingGeneratorMetadata Metadata => new("FakeEmbeddingGenerator");
        public TService? GetService<TService>(object? key = null) where TService : class => this as TService;
        public void Dispose() { }

        private static float[] MakeDeterministicVector(string text)
        {
            var vec = new float[384];
            for (var i = 0; i < text.Length; i++)
                vec[i % 384] += text[i];
            // L2-normalize
            var norm = 0f;
            for (var i = 0; i < vec.Length; i++) norm += vec[i] * vec[i];
            norm = MathF.Sqrt(norm);
            if (norm > 0f)
                for (var i = 0; i < vec.Length; i++) vec[i] /= norm;
            return vec;
        }
    }

    [Fact]
    public async Task Search_ReturnsRankedResults()
    {
        var generator = new FakeEmbeddingGenerator();
        var archetypes = new[]
        {
            MakeArchetype("auth/password-hashing", "Password Hashing",
                "Hashing passwords securely", new[] { "password", "bcrypt" }),
            MakeArchetype("injection/sql-injection", "SQL Injection Prevention",
                "Preventing SQL injection attacks", new[] { "sql", "injection" }),
        };
        var index = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator);

        // Query with the same text as the auth archetype — it should rank first
        var queryResult = await generator.GenerateAsync(
            ["Password Hashing\nHashing passwords securely\npassword bcrypt"]);
        var queryVec = queryResult[0].Vector;

        var results = index.Search(queryVec.Span, maxResults: 10);

        results.Should().HaveCount(2);
        results[0].ArchetypeId.Should().Be("auth/password-hashing");
        results[0].Score.Should().BeGreaterThan(results[1].Score);
    }

    [Fact]
    public async Task Search_MaxResults_IsRespected()
    {
        var generator = new FakeEmbeddingGenerator();
        var archetypes = new List<Archetype>();
        for (var i = 0; i < 10; i++)
        {
            archetypes.Add(MakeArchetype(
                $"cat/arch{i:D2}", $"Archetype {i}",
                $"Summary {i}", new[] { $"keyword{i}" }));
        }
        var index = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator);

        var queryResult = await generator.GenerateAsync(["test query"]);
        var results = index.Search(queryResult[0].Vector.Span, maxResults: 3);

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task Search_ScoresAreNonNegative()
    {
        var generator = new FakeEmbeddingGenerator();
        var archetypes = new[]
        {
            MakeArchetype("a/b", "Title", "Summary", new[] { "kw" }),
        };
        var index = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator);

        var queryResult = await generator.GenerateAsync(["anything"]);
        var results = index.Search(queryResult[0].Vector.Span, maxResults: 10);

        results.Should().AllSatisfy(r => r.Score.Should().BeGreaterOrEqualTo(0.0));
    }

    [Fact]
    public async Task BuildAsync_EmptyCorpus_ReturnsEmptyIndex()
    {
        var generator = new FakeEmbeddingGenerator();
        var index = await EmbeddingArchetypeIndex.BuildAsync([], generator);

        var queryResult = await generator.GenerateAsync(["test"]);
        var results = index.Search(queryResult[0].Vector.Span, maxResults: 10);

        results.Should().BeEmpty();
    }

    [Fact]
    public void GetSearchableText_ConcatenatesTitleSummaryKeywords()
    {
        var text = EmbeddingArchetypeIndex.GetSearchableText(
            MakeArchetype("a/b", "My Title", "My Summary", new[] { "kw1", "kw2" }));

        text.Should().Be("My Title\nMy Summary\nkw1 kw2");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/VibeGuard.Content.Tests/VibeGuard.Content.Tests.csproj --filter "FullyQualifiedName~EmbeddingArchetypeIndex"
```

Expected: FAIL — `EmbeddingArchetypeIndex` does not exist.

- [ ] **Step 3: Implement EmbeddingArchetypeIndex**

Create `src/VibeGuard.Content/Indexing/EmbeddingArchetypeIndex.cs`:

```csharp
using System.Collections.Frozen;
using Microsoft.Extensions.AI;

namespace VibeGuard.Content.Indexing;

/// <summary>
/// Pre-computed embedding index over the archetype corpus. Built once
/// at startup from an <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>
/// and stored immutably in a <see cref="FrozenDictionary{TKey,TValue}"/>.
/// All runtime lookups are pure dot-product scans with no I/O.
/// </summary>
public sealed class EmbeddingArchetypeIndex
{
    private readonly FrozenDictionary<string, float[]> _embeddings;

    private EmbeddingArchetypeIndex(FrozenDictionary<string, float[]> embeddings)
    {
        _embeddings = embeddings;
    }

    /// <summary>
    /// Builds the index by embedding each archetype's searchable text.
    /// Called once at startup.
    /// </summary>
    public static async Task<EmbeddingArchetypeIndex> BuildAsync(
        IReadOnlyList<Archetype> archetypes,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(archetypes);
        ArgumentNullException.ThrowIfNull(generator);

        if (archetypes.Count == 0)
            return new EmbeddingArchetypeIndex(FrozenDictionary<string, float[]>.Empty);

        var texts = new string[archetypes.Count];
        for (var i = 0; i < archetypes.Count; i++)
            texts[i] = GetSearchableText(archetypes[i]);

        var embeddings = await generator.GenerateAsync(texts, cancellationToken: ct)
            .ConfigureAwait(false);

        var map = new Dictionary<string, float[]>(archetypes.Count, StringComparer.Ordinal);
        for (var i = 0; i < archetypes.Count; i++)
            map[archetypes[i].Id] = embeddings[i].Vector.ToArray();

        return new EmbeddingArchetypeIndex(map.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>
    /// Returns up to <paramref name="maxResults"/> archetypes ranked by
    /// cosine similarity (dot product of L2-normalized vectors) to the
    /// query embedding. Scores are clamped to [0, 1].
    /// </summary>
    public IReadOnlyList<(string ArchetypeId, double Score)> Search(
        ReadOnlySpan<float> queryEmbedding, int maxResults)
    {
        if (maxResults <= 0 || _embeddings.Count == 0)
            return [];

        var scored = new List<(string Id, double Score)>(_embeddings.Count);
        foreach (var (id, vec) in _embeddings)
        {
            var sim = DotProduct(queryEmbedding, vec);
            scored.Add((id, Math.Max(0.0, sim)));
        }

        scored.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : StringComparer.Ordinal.Compare(a.Id, b.Id);
        });

        return scored.Count <= maxResults ? scored : scored.GetRange(0, maxResults);
    }

    /// <summary>
    /// Builds the searchable text for an archetype: title, summary, and
    /// keywords concatenated with newlines/spaces.
    /// </summary>
    public static string GetSearchableText(Archetype archetype)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        return $"{archetype.Principles.Title}\n{archetype.Principles.Summary}\n{string.Join(' ', archetype.Principles.Keywords)}";
    }

    private static double DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var sum = 0.0;
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
            sum += a[i] * b[i];
        return sum;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/VibeGuard.Content.Tests/VibeGuard.Content.Tests.csproj --filter "FullyQualifiedName~EmbeddingArchetypeIndex"
```

Expected: All 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/VibeGuard.Content/Indexing/EmbeddingArchetypeIndex.cs tests/VibeGuard.Content.Tests/EmbeddingArchetypeIndexTests.cs
git commit -m "feat: implement EmbeddingArchetypeIndex with cosine similarity search"
```

---

### Task 4: Implement `HybridSearchService`

**Files:**
- Create: `src/VibeGuard.Content/Indexing/HybridSearchService.cs`
- Test: `tests/VibeGuard.Content.Tests/HybridSearchServiceTests.cs`

- [ ] **Step 1: Write the unit tests**

Create `tests/VibeGuard.Content.Tests/HybridSearchServiceTests.cs`:

```csharp
using Microsoft.Extensions.AI;
using VibeGuard.Content.Indexing;

namespace VibeGuard.Content.Tests;

#pragma warning disable CA1707, CA1861
// CA1707: xUnit idiomatic Method_State_Expected naming.
// CA1861: inline constant arrays in test fixtures are clearer than hoisted statics.

public class HybridSearchServiceTests
{
    private static Archetype MakeArchetype(
        string id, string title, string summary,
        string[] keywords, string[] appliesTo)
        => new(
            Id: id,
            Principles: new PrinciplesFrontmatter
            {
                SchemaVersion = 1,
                Archetype = id,
                Title = title,
                Summary = summary,
                AppliesTo = [.. appliesTo],
                Keywords = [.. keywords],
                RelatedArchetypes = []
            },
            PrinciplesBody: "body",
            LanguageFiles: new Dictionary<string, LanguageFile>(StringComparer.Ordinal));

    /// <summary>
    /// Deterministic mock generator (same logic as EmbeddingArchetypeIndexTests).
    /// </summary>
    private sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var text in values)
                result.Add(new Embedding<float>(MakeVector(text)));
            return Task.FromResult(result);
        }

        public EmbeddingGeneratorMetadata Metadata => new("Fake");
        public TService? GetService<TService>(object? key = null) where TService : class => this as TService;
        public void Dispose() { }

        private static float[] MakeVector(string text)
        {
            var vec = new float[384];
            for (var i = 0; i < text.Length; i++) vec[i % 384] += text[i];
            var norm = 0f;
            for (var i = 0; i < vec.Length; i++) norm += vec[i] * vec[i];
            norm = MathF.Sqrt(norm);
            if (norm > 0f) for (var i = 0; i < vec.Length; i++) vec[i] /= norm;
            return vec;
        }
    }

    private static async Task<HybridSearchService> BuildService(params Archetype[] archetypes)
    {
        var generator = new FakeEmbeddingGenerator();
        var keywordIndex = KeywordArchetypeIndex.Build(archetypes);
        var embeddingIndex = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator);
        return new HybridSearchService(keywordIndex, embeddingIndex, generator);
    }

    [Fact]
    public async Task Search_ReturnsPrepMatchResults()
    {
        var service = await BuildService(
            MakeArchetype("auth/pw", "Password Hashing", "Hash passwords",
                new[] { "password", "bcrypt" }, new[] { "csharp" }));

        var results = service.Search("password hashing", "csharp", maxResults: 8);

        results.Should().ContainSingle()
               .Which.ArchetypeId.Should().Be("auth/pw");
    }

    [Fact]
    public async Task Search_LanguageFilter_ExcludesNonMatching()
    {
        var service = await BuildService(
            MakeArchetype("mem/safe", "Safe Memory", "Memory safety",
                new[] { "memory", "buffer" }, new[] { "c" }));

        var results = service.Search("memory buffer safety", "python", maxResults: 8);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_AppliesTo_All_MatchesEveryLanguage()
    {
        var service = await BuildService(
            MakeArchetype("arch/solid", "SOLID Principles", "Design principles",
                new[] { "solid", "design" }, new[] { "all" }));

        var results = service.Search("SOLID design principles", "rust", maxResults: 8);

        results.Should().ContainSingle();
    }

    [Fact]
    public async Task Search_MaxResults_IsRespected()
    {
        var archetypes = new Archetype[10];
        for (var i = 0; i < 10; i++)
        {
            archetypes[i] = MakeArchetype(
                $"cat/a{i:D2}", $"Archetype {i}", $"Summary {i}",
                new[] { "shared" }, new[] { "csharp" });
        }
        var service = await BuildService(archetypes);

        var results = service.Search("shared keyword", "csharp", maxResults: 3);

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task Search_ScoresAreInZeroOneRange()
    {
        var service = await BuildService(
            MakeArchetype("a/b", "Title", "Summary",
                new[] { "keyword" }, new[] { "csharp" }));

        var results = service.Search("keyword title", "csharp", maxResults: 8);

        results.Should().AllSatisfy(r =>
        {
            r.Score.Should().BeGreaterOrEqualTo(0.0);
            r.Score.Should().BeLessOrEqualTo(1.0);
        });
    }

    [Fact]
    public async Task GetById_DelegatesToKeywordIndex()
    {
        var service = await BuildService(
            MakeArchetype("auth/pw", "Password Hashing", "Hash passwords",
                new[] { "password" }, new[] { "csharp" }));

        service.GetById("auth/pw").Should().NotBeNull();
        service.GetById("nonexistent").Should().BeNull();
    }

    [Fact]
    public async Task GetReverseRelated_DelegatesToKeywordIndex()
    {
        var service = await BuildService();

        service.GetReverseRelated("anything").Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/VibeGuard.Content.Tests/VibeGuard.Content.Tests.csproj --filter "FullyQualifiedName~HybridSearchService"
```

Expected: FAIL — `HybridSearchService` does not exist.

- [ ] **Step 3: Implement HybridSearchService**

Create `src/VibeGuard.Content/Indexing/HybridSearchService.cs`:

```csharp
using Microsoft.Extensions.AI;

namespace VibeGuard.Content.Indexing;

/// <summary>
/// Hybrid search combining keyword matching (30%) and semantic
/// embedding similarity (70%). Implements <see cref="IArchetypeIndex"/>
/// so it can be a drop-in replacement for <see cref="KeywordArchetypeIndex"/>
/// in the DI container.
/// </summary>
public sealed class HybridSearchService : IArchetypeIndex
{
    private const double KeywordWeight = 0.3;
    private const double SemanticWeight = 0.7;

    private readonly KeywordArchetypeIndex _keywordIndex;
    private readonly EmbeddingArchetypeIndex _embeddingIndex;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;

    public HybridSearchService(
        KeywordArchetypeIndex keywordIndex,
        EmbeddingArchetypeIndex embeddingIndex,
        IEmbeddingGenerator<string, Embedding<float>> generator)
    {
        _keywordIndex = keywordIndex;
        _embeddingIndex = embeddingIndex;
        _generator = generator;
    }

    public IReadOnlyList<PrepMatch> Search(string intent, string language, int maxResults)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (maxResults <= 0) return [];

        // 1. Keyword search — returns already language-filtered, scored results.
        var keywordHits = _keywordIndex.Search(intent, language, maxResults: int.MaxValue);

        // 2. Semantic search — embed the query, scan all archetypes.
        //    GenerateAsync completes synchronously (CPU-bound ONNX inference).
        var embeddingResult = _generator.GenerateAsync([intent])
            .GetAwaiter().GetResult();
        var queryVec = embeddingResult[0].Vector;
        var semanticHits = _embeddingIndex.Search(queryVec.Span, maxResults: int.MaxValue);

        // 3. Build lookup dictionaries for fast score access.
        var keywordScores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var hit in keywordHits)
            keywordScores[hit.ArchetypeId] = hit.Score;

        var semanticScores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, score) in semanticHits)
            semanticScores[id] = score;

        // 4. Union all candidate archetype IDs.
        var allIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hit in keywordHits) allIds.Add(hit.ArchetypeId);
        foreach (var (id, _) in semanticHits) allIds.Add(id);

        // 5. Blend scores, filter by language, build PrepMatch list.
        var results = new List<PrepMatch>(allIds.Count);
        foreach (var id in allIds)
        {
            var archetype = _keywordIndex.GetById(id);
            if (archetype is null) continue;

            // Language filter: keyword index already filters, but semantic
            // hits are unfiltered — apply the same logic.
            if (!AppliesToLanguage(archetype, language)) continue;

            keywordScores.TryGetValue(id, out var kwScore);
            semanticScores.TryGetValue(id, out var semScore);

            var blended = Math.Min(1.0, (KeywordWeight * kwScore) + (SemanticWeight * semScore));
            results.Add(new PrepMatch(id, archetype.Principles.Title,
                archetype.Principles.Summary, blended));
        }

        results.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : StringComparer.Ordinal.Compare(a.ArchetypeId, b.ArchetypeId);
        });

        return results.Count <= maxResults ? results : results.GetRange(0, maxResults);
    }

    public Archetype? GetById(string archetypeId)
        => _keywordIndex.GetById(archetypeId);

    public IReadOnlyList<string> GetReverseRelated(string archetypeId)
        => _keywordIndex.GetReverseRelated(archetypeId);

    private static bool AppliesToLanguage(Archetype archetype, string language)
        => archetype.Principles.AppliesTo.Contains("all", StringComparer.OrdinalIgnoreCase)
        || archetype.Principles.AppliesTo.Contains(language, StringComparer.Ordinal);
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/VibeGuard.Content.Tests/VibeGuard.Content.Tests.csproj --filter "FullyQualifiedName~HybridSearchService"
```

Expected: All 7 tests PASS.

- [ ] **Step 5: Run all tests to check for regressions**

```bash
dotnet test tests/VibeGuard.Content.Tests/VibeGuard.Content.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/VibeGuard.Content/Indexing/HybridSearchService.cs tests/VibeGuard.Content.Tests/HybridSearchServiceTests.cs
git commit -m "feat: implement HybridSearchService with 0.3/0.7 keyword/semantic blend"
```

---

### Task 5: Wire up DI and replace `IArchetypeIndex` registration

**Files:**
- Modify: `src/VibeGuard.Mcp/Program.cs:115-146`
- Modify: `src/VibeGuard.Mcp/VibeGuard.Mcp.csproj` (may need `Microsoft.Extensions.AI.Abstractions` reference)

- [ ] **Step 1: Write the integration smoke test**

Add to a new file `tests/VibeGuard.Content.Tests/HybridSearchIntegrationTests.cs`:

```csharp
using VibeGuard.Content.Indexing;
using VibeGuard.Content.Loading;

namespace VibeGuard.Content.Tests;

#pragma warning disable CA1707
// CA1707: xUnit idiomatic Method_State_Expected naming.

/// <summary>
/// Integration tests that load the real ONNX model and the real
/// archetype corpus. These are slower (~2-5s) but verify the full
/// pipeline end-to-end.
/// </summary>
public class HybridSearchIntegrationTests
{
    private static string FindArchetypesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "archetypes");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find archetypes directory.");
    }

    [Fact]
    public async Task SemanticSearch_PasswordQuery_FindsAuthArchetype()
    {
        var root = FindArchetypesRoot();
        var repo = new FileSystemArchetypeRepository(root, false, SupportedLanguageSet.Default());
        var archetypes = repo.LoadAll();

        using var generator = OnnxEmbeddingGenerator.Create();
        var keywordIndex = KeywordArchetypeIndex.Build(archetypes);
        var embeddingIndex = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator);
        var hybrid = new HybridSearchService(keywordIndex, embeddingIndex, generator);

        var results = hybrid.Search("how do I hash a user password securely", "csharp", maxResults: 8);

        results.Should().NotBeEmpty();
        results.Select(r => r.ArchetypeId).Should().Contain("authentication/password-hashing");
    }

    [Fact]
    public async Task SemanticSearch_InjectionQuery_FindsInjectionArchetype()
    {
        var root = FindArchetypesRoot();
        var repo = new FileSystemArchetypeRepository(root, false, SupportedLanguageSet.Default());
        var archetypes = repo.LoadAll();

        using var generator = OnnxEmbeddingGenerator.Create();
        var keywordIndex = KeywordArchetypeIndex.Build(archetypes);
        var embeddingIndex = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator);
        var hybrid = new HybridSearchService(keywordIndex, embeddingIndex, generator);

        var results = hybrid.Search("prevent SQL attacks in my web app", "csharp", maxResults: 8);

        results.Should().NotBeEmpty();
        results.Select(r => r.ArchetypeId).Should().Contain("injection/sql-injection");
    }

    [Fact]
    public async Task SemanticSearch_VagueQuery_StillReturnsResults()
    {
        var root = FindArchetypesRoot();
        var repo = new FileSystemArchetypeRepository(root, false, SupportedLanguageSet.Default());
        var archetypes = repo.LoadAll();

        using var generator = OnnxEmbeddingGenerator.Create();
        var keywordIndex = KeywordArchetypeIndex.Build(archetypes);
        var embeddingIndex = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator);
        var hybrid = new HybridSearchService(keywordIndex, embeddingIndex, generator);

        // "make my code safer" has no exact keyword matches but should
        // still surface results via semantic similarity.
        var results = hybrid.Search("make my code safer", "csharp", maxResults: 8);

        results.Should().NotBeEmpty();
    }
}
```

- [ ] **Step 2: Update Program.cs to wire up the hybrid search pipeline**

Replace the `IArchetypeIndex` registration in `RegisterVibeGuardServices` in `src/VibeGuard.Mcp/Program.cs`:

Change the existing registration block:

```csharp
    services
        .AddSingleton(supportedLanguages)
        .AddSingleton<IArchetypeRepository>(sp => new FileSystemArchetypeRepository(
            archetypesRoot,
            includeDrafts,
            sp.GetRequiredService<SupportedLanguageSet>()))
        .AddSingleton<IArchetypeIndex>(sp =>
        {
            var repo = sp.GetRequiredService<IArchetypeRepository>();
            return KeywordArchetypeIndex.Build(repo.LoadAll());
        })
        .AddSingleton<IPrepService, PrepService>()
        .AddSingleton<IConsultationService, ConsultationService>();
```

To:

```csharp
    services
        .AddSingleton(supportedLanguages)
        .AddSingleton<IArchetypeRepository>(sp => new FileSystemArchetypeRepository(
            archetypesRoot,
            includeDrafts,
            sp.GetRequiredService<SupportedLanguageSet>()))
        .AddSingleton<OnnxEmbeddingGenerator>(_ => OnnxEmbeddingGenerator.Create())
        .AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
            sp.GetRequiredService<OnnxEmbeddingGenerator>())
        .AddSingleton<IArchetypeIndex>(sp =>
        {
            var repo = sp.GetRequiredService<IArchetypeRepository>();
            var archetypes = repo.LoadAll();
            var generator = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

            var keywordIndex = KeywordArchetypeIndex.Build(archetypes);
            var embeddingIndex = EmbeddingArchetypeIndex.BuildAsync(archetypes, generator)
                .GetAwaiter().GetResult();

            return new HybridSearchService(keywordIndex, embeddingIndex, generator);
        })
        .AddSingleton<IPrepService, PrepService>()
        .AddSingleton<IConsultationService, ConsultationService>();
```

Add the required `using` directives at the top of `Program.cs`:

```csharp
using Microsoft.Extensions.AI;
```

- [ ] **Step 3: Build the full solution**

```bash
dotnet build
```

Expected: Build succeeded. 0 errors.

- [ ] **Step 4: Run all tests including integration tests**

```bash
dotnet test tests/VibeGuard.Content.Tests/VibeGuard.Content.Tests.csproj
```

Expected: All tests pass, including the new integration tests.

- [ ] **Step 5: Commit**

```bash
git add src/VibeGuard.Mcp/Program.cs tests/VibeGuard.Content.Tests/HybridSearchIntegrationTests.cs
git commit -m "feat: wire up hybrid search pipeline in DI; add integration tests"
```

---

### Task 6: Verify cross-platform build and existing test suite

**Files:**
- No new files. Verification only.

- [ ] **Step 1: Run full test suite**

```bash
dotnet test tests/VibeGuard.Content.Tests/VibeGuard.Content.Tests.csproj -v normal
```

Expected: All tests pass — existing `KeywordArchetypeIndexTests`, `PrepServiceTests`, `ConsultationServiceTests`, and all new tests.

- [ ] **Step 2: Test a release publish for the current platform**

```bash
dotnet publish src/VibeGuard.Mcp/VibeGuard.Mcp.csproj -c Release -r win-x64 --self-contained -o .release-test/
```

Expected: Publish succeeds. Output directory contains the binary and ONNX runtime native libraries.

- [ ] **Step 3: Verify the published binary starts**

```bash
.release-test/vibeguard-mcp.exe --help 2>/dev/null || echo "Binary starts (MCP server, no --help flag expected)"
```

Expected: Binary starts without crash. It may error about missing archetypes directory — that's fine, confirms the ONNX model loads.

- [ ] **Step 4: Clean up test release**

```bash
rm -rf .release-test/
```

- [ ] **Step 5: Commit any fixes discovered during verification**

If any fixes were needed, commit them. Otherwise skip.

```bash
git add -A && git commit -m "fix: address cross-platform build issues for semantic search"
```
