using System.Collections.Frozen;

namespace GuardCode.Content;

/// <summary>
/// Typed YAML frontmatter for an archetype's <c>_principles.md</c> file.
/// Fields map 1:1 to design spec §4.1. Immutable record projected from
/// a file-scoped mutable DTO inside <c>Loading.FrontmatterParser</c>
/// after strict YamlDotNet deserialization.
/// </summary>
public sealed record PrinciplesFrontmatter
{
    public int SchemaVersion { get; init; }
    public string Archetype { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> AppliesTo { get; init; } = [];
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public IReadOnlyList<string> RelatedArchetypes { get; init; } = [];
    public IReadOnlyDictionary<string, string> EquivalentsIn { get; init; } = FrozenDictionary<string, string>.Empty;
    public IReadOnlyDictionary<string, string> References { get; init; } = FrozenDictionary<string, string>.Empty;
}
