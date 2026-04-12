using System.Collections.Frozen;
using Microsoft.Extensions.AI;

namespace VibeGuard.Content.Indexing;

/// <summary>
/// Pre-computed embedding index over the archetype corpus. Built once
/// at startup from an <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>
/// and stored immutably in a <see cref="FrozenDictionary{TKey,TValue}"/>.
/// All runtime lookups are pure dot-product scans with no I/O.
/// </summary>
public sealed class EmbeddingArchetypeIndex
{
    private readonly FrozenDictionary<string, float[]> _embeddings;

    private EmbeddingArchetypeIndex(FrozenDictionary<string, float[]> embeddings)
    {
        _embeddings = embeddings;
    }

    /// <summary>
    /// Builds the index by embedding each archetype's searchable text.
    /// Called once at startup.
    /// </summary>
    public static async Task<EmbeddingArchetypeIndex> BuildAsync(
        IReadOnlyList<Archetype> archetypes,
        IEmbeddingGenerator<string, Embedding<float>> generator,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(archetypes);
        ArgumentNullException.ThrowIfNull(generator);

        if (archetypes.Count == 0)
            return new EmbeddingArchetypeIndex(FrozenDictionary<string, float[]>.Empty);

        var texts = new string[archetypes.Count];
        for (var i = 0; i < archetypes.Count; i++)
            texts[i] = GetSearchableText(archetypes[i]);

        var embeddings = await generator.GenerateAsync(texts, cancellationToken: ct)
            .ConfigureAwait(false);

        var map = new Dictionary<string, float[]>(archetypes.Count, StringComparer.Ordinal);
        for (var i = 0; i < archetypes.Count; i++)
            map[archetypes[i].Id] = embeddings[i].Vector.ToArray();

        return new EmbeddingArchetypeIndex(map.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>
    /// Returns up to <paramref name="maxResults"/> archetypes ranked by
    /// cosine similarity (dot product of L2-normalized vectors) to the
    /// query embedding. Scores are clamped to [0, 1].
    /// </summary>
    public IReadOnlyList<(string ArchetypeId, double Score)> Search(
        ReadOnlySpan<float> queryEmbedding, int maxResults)
    {
        if (maxResults <= 0 || _embeddings.Count == 0)
            return [];

        var scored = new List<(string Id, double Score)>(_embeddings.Count);
        foreach (var (id, vec) in _embeddings)
        {
            var sim = DotProduct(queryEmbedding, vec);
            scored.Add((id, Math.Max(0.0, sim)));
        }

        scored.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : StringComparer.Ordinal.Compare(a.Id, b.Id);
        });

        return scored.Count <= maxResults ? scored : scored.GetRange(0, maxResults);
    }

    /// <summary>
    /// Builds the searchable text for an archetype: title, summary, and
    /// keywords concatenated with newlines/spaces.
    /// </summary>
    public static string GetSearchableText(Archetype archetype)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        return $"{archetype.Principles.Title}\n{archetype.Principles.Summary}\n{string.Join(' ', archetype.Principles.Keywords)}";
    }

    private static double DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var sum = 0.0;
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
            sum += a[i] * b[i];
        return sum;
    }
}
