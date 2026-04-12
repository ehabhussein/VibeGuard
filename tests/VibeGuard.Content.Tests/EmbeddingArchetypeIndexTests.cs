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

#pragma warning disable CA1822
    // CA1822: Metadata is an interface property; must remain instance member.
    private sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var text in values)
                result.Add(new Embedding<float>(MakeDeterministicVector(text)));
            return Task.FromResult(result);
        }

        public EmbeddingGeneratorMetadata Metadata => new("FakeEmbeddingGenerator");
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? serviceKey = null) where TService : class => this as TService;
        public void Dispose() { }

        private static float[] MakeDeterministicVector(string text)
        {
            var vec = new float[384];
            for (var i = 0; i < text.Length; i++)
                vec[i % 384] += text[i];
            var norm = 0f;
            for (var i = 0; i < vec.Length; i++) norm += vec[i] * vec[i];
            norm = MathF.Sqrt(norm);
            if (norm > 0f)
                for (var i = 0; i < vec.Length; i++) vec[i] /= norm;
            return vec;
        }
    }
#pragma warning restore CA1822

    [Fact]
    public async Task Search_ReturnsRankedResults()
    {
        var ct = TestContext.Current.CancellationToken;
        using var generator = new FakeEmbeddingGenerator();
        var archetypes = new[]
        {
            MakeArchetype("auth/password-hashing", "Password Hashing",
                "Hashing passwords securely", new[] { "password", "bcrypt" }),
            MakeArchetype("injection/sql-injection", "SQL Injection Prevention",
                "Preventing SQL injection attacks", new[] { "sql", "injection" }),
        };
        var index = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator, ct);

        var queryResult = await generator.GenerateAsync(
            ["Password Hashing\nHashing passwords securely\npassword bcrypt"], cancellationToken: ct);
        var queryVec = queryResult[0].Vector;

        var results = index.Search(queryVec.Span, maxResults: 10);

        results.Should().HaveCount(2);
        results[0].ArchetypeId.Should().Be("auth/password-hashing");
        results[0].Score.Should().BeGreaterThan(results[1].Score);
    }

    [Fact]
    public async Task Search_MaxResults_IsRespected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var generator = new FakeEmbeddingGenerator();
        var archetypes = new List<Archetype>();
        for (var i = 0; i < 10; i++)
        {
            archetypes.Add(MakeArchetype(
                $"cat/arch{i:D2}", $"Archetype {i}",
                $"Summary {i}", new[] { $"keyword{i}" }));
        }
        var index = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator, ct);

        var queryResult = await generator.GenerateAsync(["test query"], cancellationToken: ct);
        var results = index.Search(queryResult[0].Vector.Span, maxResults: 3);

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task Search_ScoresAreNonNegative()
    {
        var ct = TestContext.Current.CancellationToken;
        using var generator = new FakeEmbeddingGenerator();
        var archetypes = new[]
        {
            MakeArchetype("a/b", "Title", "Summary", new[] { "kw" }),
        };
        var index = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator, ct);

        var queryResult = await generator.GenerateAsync(["anything"], cancellationToken: ct);
        var results = index.Search(queryResult[0].Vector.Span, maxResults: 10);

        results.Should().AllSatisfy(r => r.Score.Should().BeGreaterThanOrEqualTo(0.0));
    }

    [Fact]
    public async Task BuildAsync_EmptyCorpus_ReturnsEmptyIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        using var generator = new FakeEmbeddingGenerator();
        var index = await EmbeddingArchetypeIndex.BuildAsync([], generator, ct);

        var queryResult = await generator.GenerateAsync(["test"], cancellationToken: ct);
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
