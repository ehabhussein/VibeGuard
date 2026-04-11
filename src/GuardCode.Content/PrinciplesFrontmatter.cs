// YamlDotNet requires concrete mutable collection types for deserialization.
#pragma warning disable CA1002 // Do not expose generic lists
#pragma warning disable CA2227 // Collection properties should be read only

namespace GuardCode.Content;

/// <summary>
/// Typed YAML frontmatter for an archetype's <c>_principles.md</c> file.
/// Fields map 1:1 to design spec §4.1. Property initialization with
/// <c>required</c> is enforced by the strict YamlDotNet deserializer
/// configured in <c>Loading.FrontmatterParser</c>.
/// </summary>
public sealed class PrinciplesFrontmatter
{
    public int SchemaVersion { get; set; }
    public string Archetype { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> AppliesTo { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public List<string> RelatedArchetypes { get; set; } = new();
    public Dictionary<string, string> EquivalentsIn { get; set; } = new();
    public Dictionary<string, string> References { get; set; } = new();
}
