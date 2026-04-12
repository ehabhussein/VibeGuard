using VibeGuard.Content;
using VibeGuard.Content.Indexing;
using VibeGuard.Content.Services;

namespace VibeGuard.Content.Tests;

#pragma warning disable CA1707, CA1861, CA1859
// CA1707: xUnit idiomatic Method_State_Expected naming.
// CA1861: inline constant arrays in test fixtures are clearer than hoisted statics.
// CA1859: BuildIndex returns IArchetypeIndex intentionally — tests must exercise the interface contract, not the concrete type.

public class PrepServiceTests
{
    private static readonly SupportedLanguageSet DefaultLanguages = SupportedLanguageSet.Default();

    private static IArchetypeIndex BuildIndex(params Archetype[] archetypes)
        => KeywordArchetypeIndex.Build(archetypes);

    private static PrepService BuildService(params Archetype[] archetypes)
        => new(BuildIndex(archetypes), DefaultLanguages);

    private static Archetype Make(
        string id,
        string title,
        string[] keywords,
        string[] appliesTo)
        => new(
            Id: id,
            Principles: new PrinciplesFrontmatter
            {
                SchemaVersion = 1,
                Archetype = id,
                Title = title,
                Summary = title + " summary.",
                AppliesTo = [.. appliesTo],
                Keywords = [.. keywords],
                RelatedArchetypes = []
            },
            PrinciplesBody: "body",
            LanguageFiles: new Dictionary<string, LanguageFile>(StringComparer.Ordinal));

    [Fact]
    public async Task Prep_ValidIntent_ReturnsMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = BuildService(
            Make("auth/password-hashing", "Password Hashing",
                new[] { "password", "bcrypt" }, new[] { "csharp", "python" }));

        var result = await service.PrepAsync(
            intent: "I'm writing a function to hash a password",
            language: "python",
            framework: null,
            ct);

        result.Matches.Should().ContainSingle()
              .Which.ArchetypeId.Should().Be("auth/password-hashing");
    }

    [Fact]
    public async Task Prep_EmptyIntent_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = BuildService();
        var act = () => service.PrepAsync("", "csharp", null, ct);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*non-empty*");
    }

    [Fact]
    public async Task Prep_OversizedIntent_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = BuildService();
        var giant = new string('x', PrepService.MaxIntentLength + 1);
        var act = () => service.PrepAsync(giant, "csharp", null, ct);
        await act.Should().ThrowAsync<ArgumentException>()
           .WithMessage("*characters or fewer*");
    }

    [Fact]
    public async Task Prep_LanguageFilter_HidesUnsupportedArchetypes()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = BuildService(
            Make("memory/safe-string-handling", "Safe Strings",
                new[] { "string", "buffer", "overflow" }, new[] { "c" }));

        var result = await service.PrepAsync(
            "safe string buffer handling",
            "python", // not in applies_to
            framework: null,
            ct);

        result.Matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Prep_LanguageNotInSet_ThrowsWithConfiguredListInMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = BuildService();

        var act = () => service.PrepAsync("hashing and passwords", "klingon", null, ct);

        var ex = await act.Should().ThrowAsync<ArgumentException>()
           .WithMessage("*'klingon'*not supported*");
        ex.And.Message.Should().Contain("csharp");
        ex.And.Message.Should().Contain("rust");
    }

    [Fact]
    public async Task Prep_RestrictedLanguageSet_RejectsOtherwiseValidLanguage()
    {
        var ct = TestContext.Current.CancellationToken;
        // Rebuild the service with a set that excludes python entirely.
        var cSharpOnly = new SupportedLanguageSet(["csharp"]);
        var service = new PrepService(
            BuildIndex(
                Make("auth/password-hashing", "Password Hashing",
                    new[] { "password" }, new[] { "csharp", "python" })),
            cSharpOnly);

        var act = () => service.PrepAsync("hash a password", "python", null, ct);

        await act.Should().ThrowAsync<ArgumentException>()
           .WithMessage("*python*not supported*");
    }
}
