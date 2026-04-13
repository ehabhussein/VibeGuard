using Microsoft.Extensions.AI;
using VibeGuard.Content.Indexing;

namespace VibeGuard.Content.Tests;

#pragma warning disable CA1707
// CA1707: xUnit idiomatic Method_State_Expected naming.

public class OnnxEmbeddingGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_SingleInput_Returns384DimVector()
    {
        using var generator = OnnxEmbeddingGenerator.Create();
        var ct = TestContext.Current.CancellationToken;

        var result = await generator.GenerateAsync(["password hashing with bcrypt"], cancellationToken: ct);

        result.Should().ContainSingle();
        result[0].Vector.Length.Should().Be(384);
    }

    [Fact]
    public async Task GenerateAsync_OutputIsL2Normalized()
    {
        using var generator = OnnxEmbeddingGenerator.Create();
        var ct = TestContext.Current.CancellationToken;

        var result = await generator.GenerateAsync(["secure coding practices"], cancellationToken: ct);

        var vec = result[0].Vector;
        var norm = 0.0;
        for (var i = 0; i < vec.Length; i++)
            norm += vec.Span[i] * vec.Span[i];
        norm = Math.Sqrt(norm);

        norm.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public async Task GenerateAsync_SimilarInputs_HaveHighCosineSimilarity()
    {
        using var generator = OnnxEmbeddingGenerator.Create();
        var ct = TestContext.Current.CancellationToken;

        var results = await generator.GenerateAsync([
            "hash a password securely",
            "secure password hashing"
        ], cancellationToken: ct);

        var similarity = CosineSimilarity(results[0].Vector.Span, results[1].Vector.Span);
        similarity.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public async Task GenerateAsync_DissimilarInputs_HaveLowCosineSimilarity()
    {
        using var generator = OnnxEmbeddingGenerator.Create();
        var ct = TestContext.Current.CancellationToken;

        var results = await generator.GenerateAsync([
            "hash a password securely",
            "TCP socket connection pooling"
        ], cancellationToken: ct);

        var similarity = CosineSimilarity(results[0].Vector.Span, results[1].Vector.Span);
        similarity.Should().BeLessThan(0.6);
    }

    [Fact]
    public async Task GenerateAsync_MultipleInputs_ReturnsSameCount()
    {
        using var generator = OnnxEmbeddingGenerator.Create();
        var ct = TestContext.Current.CancellationToken;

        var inputs = new[] { "input one", "input two", "input three" };
        var result = await generator.GenerateAsync(inputs, cancellationToken: ct);

        result.Should().HaveCount(3);
    }

    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var dot = 0.0;
        for (var i = 0; i < a.Length; i++)
            dot += a[i] * b[i];
        return dot; // Vectors are L2-normalized, so dot product = cosine similarity.
    }
}
