// xUnit test methods intentionally use Method_State_Expected underscored naming
// for readability in test runner output; CA1707 does not apply to test fixtures.
#pragma warning disable CA1707 // Identifiers should not contain underscores
// Inline literal arrays in assertions are clearer than hoisting to fields for
// one-off expected values; CA1861's perf concern is irrelevant in test code.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

using GuardCode.Content;
using GuardCode.Content.Loading;

namespace GuardCode.Content.Tests;

public class FrontmatterParserTests
{
    private const string ValidPrinciples =
        """
        ---
        schema_version: 1
        archetype: auth/password-hashing
        title: Password Hashing
        summary: Storing, verifying, and handling user passwords in any backend.
        applies_to: [csharp, python, go]
        keywords:
          - password
          - bcrypt
        related_archetypes:
          - auth/session-tokens
        equivalents_in:
          c: crypto/key-derivation
        references:
          owasp_asvs: V2.4
          cwe: "916"
        ---

        # Body

        Principles body text.
        """;

    [Fact]
    public void Parse_ValidPrinciples_ReturnsFrontmatterAndBody()
    {
        var result = FrontmatterParser.ParsePrinciples(ValidPrinciples);

        result.Frontmatter.SchemaVersion.Should().Be(1);
        result.Frontmatter.Archetype.Should().Be("auth/password-hashing");
        result.Frontmatter.Title.Should().Be("Password Hashing");
        result.Frontmatter.AppliesTo.Should().BeEquivalentTo(new[] { "csharp", "python", "go" });
        result.Frontmatter.Keywords.Should().BeEquivalentTo(new[] { "password", "bcrypt" });
        result.Frontmatter.RelatedArchetypes.Should().ContainSingle().Which.Should().Be("auth/session-tokens");
        result.Frontmatter.EquivalentsIn.Should().ContainKey("c").WhoseValue.Should().Be("crypto/key-derivation");
        result.Frontmatter.References.Should().ContainKey("owasp_asvs").WhoseValue.Should().Be("V2.4");
        result.Body.Should().StartWith("# Body");
        result.Body.Should().Contain("Principles body text.");
        result.Body.Should().NotContain("schema_version");
    }

    [Fact]
    public void Parse_MissingOpeningDelimiter_Throws()
    {
        const string content = "no frontmatter here\njust body.";
        var act = () => FrontmatterParser.ParsePrinciples(content);
        act.Should().Throw<FrontmatterParseException>()
           .WithMessage("*does not begin*");
    }

    [Fact]
    public void Parse_UnclosedFrontmatter_Throws()
    {
        const string content =
            """
            ---
            schema_version: 1
            archetype: x/y
            """;
        var act = () => FrontmatterParser.ParsePrinciples(content);
        act.Should().Throw<FrontmatterParseException>()
           .WithMessage("*not closed*");
    }

    [Fact]
    public void Parse_UnknownField_Throws()
    {
        const string content =
            """
            ---
            schema_version: 1
            archetype: x/y
            title: T
            summary: s
            applies_to: [csharp]
            keywords: [k]
            unexpected_field: boom
            ---

            body
            """;
        var act = () => FrontmatterParser.ParsePrinciples(content);
        act.Should().Throw<FrontmatterParseException>()
           .WithMessage("*malformed or contains unknown fields*");
    }

    [Fact]
    public void Parse_MalformedYaml_Throws()
    {
        const string content =
            """
            ---
            schema_version: [not, a, number
            ---

            body
            """;
        var act = () => FrontmatterParser.ParsePrinciples(content);
        act.Should().Throw<FrontmatterParseException>();
    }
}
