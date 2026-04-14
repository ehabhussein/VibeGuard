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
        var ct = TestContext.Current.CancellationToken;
        var root = FindArchetypesRoot();
        var repo = new FileSystemArchetypeRepository(root, false, SupportedLanguageSet.Default());
        var archetypes = repo.LoadAll();

        using var generator = OnnxEmbeddingGenerator.Create();
        var keywordIndex = KeywordArchetypeIndex.Build(archetypes);
        var embeddingIndex = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator, ct);
        var hybrid = new HybridSearchService(keywordIndex, embeddingIndex, generator);

        var results = await hybrid.SearchAsync("how do I hash a user password securely", "csharp", maxResults: 15, ct);

        results.Should().NotBeEmpty();
        results.Select(r => r.ArchetypeId).Should().Contain("auth/password-hashing");
    }

    [Fact]
    public async Task SemanticSearch_InjectionQuery_FindsInjectionArchetype()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = FindArchetypesRoot();
        var repo = new FileSystemArchetypeRepository(root, false, SupportedLanguageSet.Default());
        var archetypes = repo.LoadAll();

        using var generator = OnnxEmbeddingGenerator.Create();
        var keywordIndex = KeywordArchetypeIndex.Build(archetypes);
        var embeddingIndex = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator, ct);
        var hybrid = new HybridSearchService(keywordIndex, embeddingIndex, generator);

        var results = await hybrid.SearchAsync("prevent SQL attacks in my web app", "csharp", maxResults: 15, ct);

        results.Should().NotBeEmpty();
        results.Select(r => r.ArchetypeId).Should().Contain("persistence/sql-injection");
    }

    [Fact]
    public async Task SemanticSearch_VagueQuery_StillReturnsResults()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = FindArchetypesRoot();
        var repo = new FileSystemArchetypeRepository(root, false, SupportedLanguageSet.Default());
        var archetypes = repo.LoadAll();

        using var generator = OnnxEmbeddingGenerator.Create();
        var keywordIndex = KeywordArchetypeIndex.Build(archetypes);
        var embeddingIndex = await EmbeddingArchetypeIndex.BuildAsync(archetypes, generator, ct);
        var hybrid = new HybridSearchService(keywordIndex, embeddingIndex, generator);

        // "make my code safer" has no exact keyword matches but should
        // still surface results via semantic similarity.
        var results = await hybrid.SearchAsync("make my code safer", "csharp", maxResults: 15, ct);

        results.Should().NotBeEmpty();
    }

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

    [Fact]
    public async Task SemanticSearch_GreenfieldQuery_SurfacesEngineeringArchetypes()
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
            "I am starting a brand new greenfield Go service from scratch and want to " +
            "structure it properly with modules, a walking skeleton, and not over-engineer",
            "go", maxResults: 15, ct);

        var ids = results.Select(r => r.ArchetypeId).ToList();
        ids.Should().Contain(id => id.StartsWith("engineering/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SemanticSearch_RefactorQuery_SurfacesEngineeringRefactoring()
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
            "I want to refactor a large tangled module and split its responsibilities cleanly",
            "csharp", maxResults: 15, ct);

        var ids = results.Select(r => r.ArchetypeId).ToList();
        ids.Should().Contain("engineering/refactoring-discipline");
        ids.Should().Contain("engineering/module-decomposition");
    }

    [Fact]
    public async Task SemanticSearch_ErrorHandlingQuery_SurfacesErrorHandlingArchetype()
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
            "how should I handle exceptions and propagate failures across service boundaries",
            "csharp", maxResults: 15, ct);

        var ids = results.Select(r => r.ArchetypeId).ToList();
        ids.Should().Contain("engineering/error-handling");
    }

    [Fact]
    public async Task SemanticSearch_ConfigQuery_SurfacesConfigurationManagement()
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
            "loading environment variables and separating secrets from settings at service startup",
            "csharp", maxResults: 15, ct);

        var ids = results.Select(r => r.ArchetypeId).ToList();
        ids.Should().Contain("engineering/configuration-management");
    }

    [Fact]
    public async Task SemanticSearch_DataModelingQuery_SurfacesDataModeling()
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
            "designing the schema for a new orders and customers domain with primary keys and timestamps",
            "csharp", maxResults: 15, ct);

        var ids = results.Select(r => r.ArchetypeId).ToList();
        ids.Should().Contain("engineering/data-modeling");
    }

    [Fact]
    public async Task SemanticSearch_PostmortemQuery_SurfacesIncidentResponse()
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
            "we had a production outage last night, how should we run a blameless postmortem",
            "csharp", maxResults: 15, ct);

        var ids = results.Select(r => r.ArchetypeId).ToList();
        ids.Should().Contain("engineering/incident-response");
    }

    [Fact]
    public async Task SemanticSearch_CloudCostQuery_SurfacesCostAwareness()
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
            "our cloud bill is growing fast and we need to attribute cost per feature",
            "csharp", maxResults: 15, ct);

        var ids = results.Select(r => r.ArchetypeId).ToList();
        ids.Should().Contain("engineering/cost-awareness");
    }
}
