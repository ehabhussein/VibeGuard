using System.Diagnostics;

namespace VibeGuard.Content;

/// <summary>
/// Lifecycle stage of an archetype. Tiers by maturity and review, not by
/// authorship. Every archetype enters the corpus as <see cref="Draft"/>,
/// is promoted to <see cref="Stable"/> by a reviewer PR, and may later
/// be retired as <see cref="Deprecated"/> with a pointer to its successor.
/// </summary>
/// <remarks>
/// Modelled after RFC, PEP, and TC39 stage flows: the same file evolves
/// through states over time, rather than moving directories (which would
/// break IDs) or living in a second repo (which would fragment the
/// contributor experience).
/// </remarks>
public enum ArchetypeStatus
{
    /// <summary>
    /// Unreviewed or in-progress content. Hidden from the default active
    /// corpus and invisible to <c>prep</c>. Contributors and reviewers
    /// opt in with <c>VIBEGUARD_INCLUDE_DRAFTS=1</c> to exercise drafts
    /// locally.
    /// </summary>
    Draft = 0,

    /// <summary>
    /// Reviewed, vetted, and included in every default load. Default value
    /// for record construction in tests so fixtures that don't care about
    /// lifecycle semantics don't have to spell it out.
    /// </summary>
    Stable = 1,

    /// <summary>
    /// Retired content. Still loads and still resolves by ID so existing
    /// references don't break, but <c>consult</c> prepends a deprecation
    /// banner pointing at the <c>superseded_by</c> successor.
    /// </summary>
    Deprecated = 2,
}

/// <summary>
/// Wire-form round-trip for <see cref="ArchetypeStatus"/>. The wire form
/// is the lowercase token used in YAML frontmatter (<c>draft</c>,
/// <c>stable</c>, <c>deprecated</c>) and in stderr diagnostics.
/// </summary>
public static class ArchetypeStatusExtensions
{
    public const string DraftWire = "draft";
    public const string StableWire = "stable";
    public const string DeprecatedWire = "deprecated";

    // C# 14 extension members — exposes WireString as a property
    // rather than a ToWireString() method. Same semantics, modern syntax.
    //
    // CA1034: the compiler synthesizes a nested type for the extension
    // block; this is not a developer-authored nested type and will be
    // suppressed by the SDK once the analyzers catch up with C# 14.
#pragma warning disable CA1034
    extension(ArchetypeStatus status)
    {
        /// <summary>
        /// The lowercase wire-form token for this status value,
        /// matching the YAML frontmatter representation.
        /// </summary>
        public string WireString => status switch
        {
            ArchetypeStatus.Draft => DraftWire,
            ArchetypeStatus.Stable => StableWire,
            ArchetypeStatus.Deprecated => DeprecatedWire,
            _ => throw new UnreachableException(),
        };
    }
#pragma warning restore CA1034

    public static bool TryParseWire(string? wire, out ArchetypeStatus status)
    {
        switch (wire)
        {
            case DraftWire:
                status = ArchetypeStatus.Draft;
                return true;
            case StableWire:
                status = ArchetypeStatus.Stable;
                return true;
            case DeprecatedWire:
                status = ArchetypeStatus.Deprecated;
                return true;
            default:
                status = default;
                return false;
        }
    }
}
