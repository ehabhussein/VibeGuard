using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text;

namespace VibeGuard.Content.Indexing;

/// <summary>
/// Builds three frozen data structures once from the loaded corpus and
/// answers all runtime lookups from memory. The implementation is
/// deliberately keyword-based with deterministic scoring: no embeddings,
/// no fuzzy matching — those are deferred per design spec §10.
/// </summary>
public sealed class KeywordArchetypeIndex : IArchetypeIndex
{
    // Stopwords are matched case-insensitively via the frozen set's comparer,
    // so the tokenizer can stay allocation-light and skip case folding.
    private static readonly FrozenSet<string> Stopwords = new[]
    {
        "a", "an", "the", "and", "or", "but", "if", "then", "else",
        "of", "in", "on", "at", "to", "for", "with", "from", "by",
        "is", "are", "was", "were", "be", "been", "being",
        "i", "im", "my", "we", "you", "your",
        "how", "do", "does", "about", "want", "need",
        "this", "that", "these", "those",
        "it", "its", "as", "so"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly FrozenDictionary<string, Archetype> _byId;
    private readonly FrozenDictionary<string, FrozenDictionary<string, double>> _keywordIndex;
    private readonly FrozenDictionary<string, double> _idfWeights;
    private readonly FrozenDictionary<string, ImmutableArray<string>> _reverseRelated;

    private KeywordArchetypeIndex(
        FrozenDictionary<string, Archetype> byId,
        FrozenDictionary<string, FrozenDictionary<string, double>> keywordIndex,
        FrozenDictionary<string, double> idfWeights,
        FrozenDictionary<string, ImmutableArray<string>> reverseRelated)
    {
        _byId = byId;
        _keywordIndex = keywordIndex;
        _idfWeights = idfWeights;
        _reverseRelated = reverseRelated;
    }

    public static KeywordArchetypeIndex Build(IReadOnlyList<Archetype> archetypes)
    {
        ArgumentNullException.ThrowIfNull(archetypes);
        var byId = BuildByIdIndex(archetypes);
        var (keywordIndex, idfWeights) = BuildKeywordIndex(archetypes);
        var reverseRelated = BuildReverseRelatedIndex(archetypes);
        return new KeywordArchetypeIndex(byId, keywordIndex, idfWeights, reverseRelated);
    }

    public Archetype? GetById(string archetypeId)
        => _byId.TryGetValue(archetypeId, out var a) ? a : null;

    public IReadOnlyList<string> GetReverseRelated(string archetypeId)
        => _reverseRelated.TryGetValue(archetypeId, out var list) ? list : ImmutableArray<string>.Empty;

    public Task<IReadOnlyList<PrepMatch>> SearchAsync(
        string intent, string language, int maxResults, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (maxResults <= 0) return Task.FromResult<IReadOnlyList<PrepMatch>>([]);
        var tokens = Tokenize(intent);
        if (tokens.Count == 0) return Task.FromResult<IReadOnlyList<PrepMatch>>([]);

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        AccumulateKeywordScores(tokens, scores);
        AccumulateTitleSummaryBonus(tokens, scores);

        var divisor = 2.0 * Math.Log(_byId.Count + 1);
        var hits = new List<PrepMatch>(scores.Count);
        foreach (var (id, raw) in scores)
        {
            if (raw <= 0) continue;
            var archetype = _byId[id];
            if (!AppliesToLanguage(archetype, language)) continue;
            var normalized = Math.Min(1.0, raw / divisor);
            hits.Add(new PrepMatch(id, archetype.Principles.Title, archetype.Principles.Summary, normalized));
        }

        hits.Sort(CompareHits);
        IReadOnlyList<PrepMatch> result = hits.Count <= maxResults ? hits : hits.GetRange(0, maxResults);
        return Task.FromResult(result);
    }

    private static int CompareHits(PrepMatch a, PrepMatch b)
    {
        var byScore = b.Score.CompareTo(a.Score);
        return byScore != 0 ? byScore : StringComparer.Ordinal.Compare(a.ArchetypeId, b.ArchetypeId);
    }

    /// <summary>
    /// Returns true when an archetype is relevant for the given language.
    /// Archetypes with <c>applies_to: [all]</c> match every language —
    /// this supports principles-only (language-agnostic) archetypes.
    /// </summary>
    private static bool AppliesToLanguage(Archetype archetype, string language)
        => archetype.Principles.AppliesTo.Contains("all", StringComparer.OrdinalIgnoreCase)
        || archetype.Principles.AppliesTo.Contains(language, StringComparer.Ordinal);

    private static FrozenDictionary<string, Archetype> BuildByIdIndex(IReadOnlyList<Archetype> archetypes)
    {
        var map = new Dictionary<string, Archetype>(archetypes.Count, StringComparer.Ordinal);
        foreach (var archetype in archetypes)
        {
            map[archetype.Id] = archetype;
        }
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static (
        FrozenDictionary<string, FrozenDictionary<string, double>> Index,
        FrozenDictionary<string, double> Idf
    ) BuildKeywordIndex(IReadOnlyList<Archetype> archetypes)
    {
        // term → {archetypeId: matchWeight}; keep highest weight per (term, archetype) pair.
        var accumulator = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

        void Record(string term, string archetypeId, double weight)
        {
            if (!accumulator.TryGetValue(term, out var bucket))
            {
                bucket = new Dictionary<string, double>(StringComparer.Ordinal);
                accumulator[term] = bucket;
            }
            bucket.TryGetValue(archetypeId, out var existing);
            if (weight > existing) bucket[archetypeId] = weight;
        }

        foreach (var archetype in archetypes)
        {
            foreach (var keyword in archetype.Principles.Keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;

                // Exact keyword at full weight.
                Record(keyword, archetype.Id, 1.0);

                // Compound decomposition for hyphenated keywords.
                if (keyword.Contains('-'))
                {
                    foreach (var part in keyword.Split('-'))
                    {
                        if (part.Length >= 3 && !Stopwords.Contains(part))
                            Record(part, archetype.Id, 0.5);
                    }
                }
            }
        }

        // Freeze the inner dictionaries.
        var frozen = new Dictionary<string, FrozenDictionary<string, double>>(
            accumulator.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (term, bucket) in accumulator)
        {
            frozen[term] = bucket.ToFrozenDictionary(StringComparer.Ordinal);
        }
        var index = frozen.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        // IDF: log((N + 1) / (df + 1)) + 1  where df = distinct archetypes a term maps to.
        var n = archetypes.Count;
        var idf = new Dictionary<string, double>(accumulator.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (term, bucket) in accumulator)
        {
            var df = bucket.Count;
            idf[term] = Math.Log((n + 1.0) / (df + 1.0)) + 1.0;
        }
        var idfFrozen = idf.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        return (index, idfFrozen);
    }

    private static FrozenDictionary<string, ImmutableArray<string>> BuildReverseRelatedIndex(
        IReadOnlyList<Archetype> archetypes)
    {
        var accumulator = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var source in archetypes)
        {
            foreach (var target in source.Principles.RelatedArchetypes)
            {
                if (string.IsNullOrWhiteSpace(target)) continue;
                if (!accumulator.TryGetValue(target, out var bucket))
                {
                    bucket = new SortedSet<string>(StringComparer.Ordinal);
                    accumulator[target] = bucket;
                }
                bucket.Add(source.Id);
            }
        }
        var frozen = new Dictionary<string, ImmutableArray<string>>(accumulator.Count, StringComparer.Ordinal);
        foreach (var (target, sources) in accumulator)
        {
            frozen[target] = [.. sources];
        }
        return frozen.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private void AccumulateKeywordScores(IReadOnlyList<string> tokens, Dictionary<string, double> scores)
    {
        foreach (var token in tokens)
        {
            if (!_keywordIndex.TryGetValue(token, out var archetypeWeights)) continue;
            var idf = _idfWeights[token];
            foreach (var (id, matchWeight) in archetypeWeights)
            {
                scores.TryGetValue(id, out var current);
                scores[id] = current + (matchWeight * idf);
            }
        }
    }

    private void AccumulateTitleSummaryBonus(IReadOnlyList<string> tokens, Dictionary<string, double> scores)
    {
        foreach (var (id, archetype) in _byId)
        {
            var haystack = archetype.Principles.Title + " " + archetype.Principles.Summary;
            var matches = 0;
            foreach (var token in tokens)
            {
                if (haystack.Contains(token, StringComparison.OrdinalIgnoreCase)) matches++;
            }
            if (matches == 0) continue;
            scores.TryGetValue(id, out var current);
            scores[id] = current + (matches * 0.5);
        }
    }

    private static List<string> Tokenize(string intent)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(intent)) return result;
        var buffer = new StringBuilder();
        foreach (var ch in intent)
        {
            if (char.IsLetterOrDigit(ch)) { buffer.Append(ch); continue; }
            FlushToken(buffer, result);
        }
        FlushToken(buffer, result);
        return result;
    }

    private static void FlushToken(StringBuilder buffer, List<string> result)
    {
        if (buffer.Length < 2) { buffer.Clear(); return; }
        var token = buffer.ToString();
        buffer.Clear();
        if (Stopwords.Contains(token)) return;
        result.Add(token);
    }
}
