// YamlDotNet requires concrete mutable collection types for deserialization.
#pragma warning disable CA1002 // Do not expose generic lists
#pragma warning disable CA2227 // Collection properties should be read only

namespace GuardCode.Content;

/// <summary>
/// Typed YAML frontmatter for an archetype's language file
/// (<c>csharp.md</c>, <c>python.md</c>, etc.). See design spec §4.2.
/// </summary>
public sealed class LanguageFrontmatter
{
    public int SchemaVersion { get; set; }
    public string Archetype { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? Framework { get; set; }
    public string PrinciplesFile { get; set; } = string.Empty;
    public LibrariesSection Libraries { get; set; } = new();
    public Dictionary<string, string> MinimumVersions { get; set; } = new();
}

public sealed class LibrariesSection
{
    public string Preferred { get; set; } = string.Empty;
    public List<string> Acceptable { get; set; } = new();
    public List<AvoidedLibrary> Avoid { get; set; } = new();
}

public sealed class AvoidedLibrary
{
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
