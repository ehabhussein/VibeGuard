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
                result.Add(new Embedding<float>(MakeVector(text)));
            return Task.FromResult(result);
        }

        public EmbeddingGeneratorMetadata Metadata => new("Fake");
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? serviceKey = null) where TService : class => this as TService;
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
#pragma warning restore CA1822

    private static async Task<HybridSearchService> BuildService(params Archetype[] archetypes)
    {
        using var generator = new FakeEmbeddingGenerator();
        var keywordIndex = KeywordArchetypeIndex.Build(archetypes);
        var embeddingIndex = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator).ConfigureAwait(true);
        return new HybridSearchService(keywordIndex, embeddingIndex, generator);
    }

    [Fact]
    public async Task Search_ReturnsPrepMatchResults()
    {
        var service = await BuildService(
            MakeArchetype("auth/pw", "Password Hashing", "Hash passwords",
                new[] { "password", "bcrypt" }, new[] { "csharp" }));

        var ct = TestContext.Current.CancellationToken;
        var results = await service.SearchAsync("password hashing", "csharp", maxResults: 8, ct);

        results.Should().ContainSingle()
               .Which.ArchetypeId.Should().Be("auth/pw");
    }

    [Fact]
    public async Task Search_LanguageFilter_ExcludesNonMatching()
    {
        var service = await BuildService(
            MakeArchetype("mem/safe", "Safe Memory", "Memory safety",
                new[] { "memory", "buffer" }, new[] { "c" }));

        var ct = TestContext.Current.CancellationToken;
        var results = await service.SearchAsync("memory buffer safety", "python", maxResults: 8, ct);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_AppliesTo_All_MatchesEveryLanguage()
    {
        var service = await BuildService(
            MakeArchetype("arch/solid", "SOLID Principles", "Design principles",
                new[] { "solid", "design" }, new[] { "all" }));

        var ct = TestContext.Current.CancellationToken;
        var results = await service.SearchAsync("SOLID design principles", "rust", maxResults: 8, ct);

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

        var ct = TestContext.Current.CancellationToken;
        var results = await service.SearchAsync("shared keyword", "csharp", maxResults: 3, ct);

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task Search_ScoresAreInZeroOneRange()
    {
        var service = await BuildService(
            MakeArchetype("a/b", "Title", "Summary",
                new[] { "keyword" }, new[] { "csharp" }));

        var ct = TestContext.Current.CancellationToken;
        var results = await service.SearchAsync("keyword title", "csharp", maxResults: 8, ct);

        results.Should().AllSatisfy(r =>
        {
            r.Score.Should().BeGreaterThanOrEqualTo(0.0);
            r.Score.Should().BeLessThanOrEqualTo(1.0);
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
