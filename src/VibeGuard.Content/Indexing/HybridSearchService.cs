using Microsoft.Extensions.AI;

namespace VibeGuard.Content.Indexing;

/// <summary>
/// Hybrid search combining keyword matching (30%) and semantic
/// embedding similarity (70%). Implements <see cref="IArchetypeIndex"/>
/// so it can be a drop-in replacement for <see cref="KeywordArchetypeIndex"/>
/// in the DI container.
/// </summary>
public sealed class HybridSearchService : IArchetypeIndex
{
    private const double KeywordWeight = 0.3;
    private const double SemanticWeight = 0.7;
    private const string QueryPrefix = "Represent this sentence for searching relevant passages: ";

    private readonly KeywordArchetypeIndex _keywordIndex;
    private readonly EmbeddingArchetypeIndex _embeddingIndex;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;

    public HybridSearchService(
        KeywordArchetypeIndex keywordIndex,
        EmbeddingArchetypeIndex embeddingIndex,
        IEmbeddingGenerator<string, Embedding<float>> generator)
    {
        _keywordIndex = keywordIndex;
        _embeddingIndex = embeddingIndex;
        _generator = generator;
    }

    public async Task<IReadOnlyList<PrepMatch>> SearchAsync(
        string intent, string language, int maxResults, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (maxResults <= 0) return [];

        // 1. Keyword search — returns already language-filtered, scored results.
        var keywordHits = await _keywordIndex.SearchAsync(intent, language, maxResults: int.MaxValue, ct).ConfigureAwait(false);

        // 2. Semantic search — embed the query, scan all archetypes.
        var embeddingResult = await _generator.GenerateAsync([QueryPrefix + intent], cancellationToken: ct).ConfigureAwait(false);
        var queryVec = embeddingResult[0].Vector;
        var semanticHits = _embeddingIndex.Search(queryVec.Span, maxResults: int.MaxValue);

        // 3. Build lookup dictionaries for fast score access.
        var keywordScores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var hit in keywordHits)
            keywordScores[hit.ArchetypeId] = hit.Score;

        var semanticScores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, score) in semanticHits)
            semanticScores[id] = score;

        // 4. Union all candidate archetype IDs.
        var allIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hit in keywordHits) allIds.Add(hit.ArchetypeId);
        foreach (var (id, _) in semanticHits) allIds.Add(id);

        // 5. Blend scores, filter by language, build PrepMatch list.
        var results = new List<PrepMatch>(allIds.Count);
        foreach (var id in allIds)
        {
            var archetype = _keywordIndex.GetById(id);
            if (archetype is null) continue;

            // Language filter: keyword index already filters, but semantic
            // hits are unfiltered — apply the same logic.
            if (!AppliesToLanguage(archetype, language)) continue;

            keywordScores.TryGetValue(id, out var kwScore);
            semanticScores.TryGetValue(id, out var semScore);

            var blended = Math.Min(1.0, (KeywordWeight * kwScore) + (SemanticWeight * semScore));
            results.Add(new PrepMatch(id, archetype.Principles.Title,
                archetype.Principles.Summary, blended));
        }

        results.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : StringComparer.Ordinal.Compare(a.ArchetypeId, b.ArchetypeId);
        });

        return results.Count <= maxResults ? results : results.GetRange(0, maxResults);
    }

    public Archetype? GetById(string archetypeId)
        => _keywordIndex.GetById(archetypeId);

    public IReadOnlyList<string> GetReverseRelated(string archetypeId)
        => _keywordIndex.GetReverseRelated(archetypeId);

    private static bool AppliesToLanguage(Archetype archetype, string language)
        => archetype.Principles.AppliesTo.Contains("all", StringComparer.OrdinalIgnoreCase)
        || archetype.Principles.AppliesTo.Contains(language, StringComparer.Ordinal);
}
