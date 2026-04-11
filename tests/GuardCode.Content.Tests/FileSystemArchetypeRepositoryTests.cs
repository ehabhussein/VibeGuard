#pragma warning disable CA1707, CA1861
// CA1707: xUnit idiomatic Method_State_Expected naming.
// CA1861: inline constant arrays in test fixtures are clearer than hoisted statics.

using GuardCode.Content.Loading;

namespace GuardCode.Content.Tests;

public sealed class FileSystemArchetypeRepositoryTests : IDisposable
{
    private readonly string _rootDir;

    public FileSystemArchetypeRepositoryTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "guardcode-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDir))
        {
            Directory.Delete(_rootDir, recursive: true);
        }
    }

    private void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_rootDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private const string ValidPrinciples =
        """
        ---
        schema_version: 1
        archetype: auth/password-hashing
        title: Password Hashing
        summary: Summary.
        applies_to: [csharp]
        keywords: [password]
        ---

        # Password Hashing — Principles

        ## When this applies
        When storing passwords.

        ## Architectural placement
        At the auth boundary.

        ## Principles
        Use a slow KDF.

        ## Anti-patterns
        Don't use MD5.

        ## References
        OWASP ASVS V2.4.
        """;

    private const string ValidCsharp =
        """
        ---
        schema_version: 1
        archetype: auth/password-hashing
        language: csharp
        principles_file: _principles.md
        libraries:
          preferred: Konscious.Security.Cryptography.Argon2
          acceptable: []
          avoid: []
        ---

        # Password Hashing — C#

        ## Library choice
        Konscious.

        ## Reference implementation
        ```csharp
        void H() { }
        ```

        ## Language-specific gotchas
        Watch out.

        ## Tests to write
        Shape tests.
        """;

    private const string ValidInputValidationPrinciples =
        """
        ---
        schema_version: 1
        archetype: io/input-validation
        title: Input Validation
        summary: Summary.
        applies_to: [csharp]
        keywords: [input, validation]
        ---

        # Input Validation — Principles

        ## When this applies
        At every trust boundary.

        ## Architectural placement
        At edges.

        ## Principles
        Reject invalid early.

        ## Anti-patterns
        Blacklists.

        ## References
        OWASP cheat sheet.
        """;

    [Fact]
    public void LoadAll_TwoArchetypes_ReturnsBoth()
    {
        WriteFile("auth/password-hashing/_principles.md", ValidPrinciples);
        WriteFile("auth/password-hashing/csharp.md", ValidCsharp);
        WriteFile("io/input-validation/_principles.md", ValidInputValidationPrinciples);

        var repo = new FileSystemArchetypeRepository(_rootDir);
        var archetypes = repo.LoadAll();

        archetypes.Should().HaveCount(2);
        archetypes.Should().Contain(a => a.Id == "auth/password-hashing");
        archetypes.Should().Contain(a => a.Id == "io/input-validation");
    }

    [Fact]
    public void Ctor_NonExistentDirectory_Throws()
    {
        var act = () => new FileSystemArchetypeRepository(
            Path.Combine(_rootDir, "does-not-exist"));
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void LoadAll_DirectoryWithoutPrinciples_IsIgnored()
    {
        // Just a stray language file with no _principles.md — should be ignored silently,
        // not cause a validation failure, because it's not a claimed archetype yet.
        WriteFile("draft/something/csharp.md", ValidCsharp);

        var repo = new FileSystemArchetypeRepository(_rootDir);
        var archetypes = repo.LoadAll();

        archetypes.Should().BeEmpty();
    }
}
