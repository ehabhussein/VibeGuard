using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;

namespace VibeGuard.Content.Indexing;

/// <summary>
/// Local ONNX-based embedding generator using all-MiniLM-L6-v2.
/// Produces L2-normalized 384-dimensional vectors. The ONNX model
/// and BERT vocabulary are loaded from assembly embedded resources.
/// All inference is CPU-bound — <see cref="GenerateAsync"/> completes
/// synchronously and returns <see cref="Task.FromResult{TResult}"/>.
/// </summary>
public sealed class OnnxEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>, IDisposable
{
    private const string ModelResourceName = "VibeGuard.Content.Models.all-MiniLM-L6-v2.onnx";
    private const string VocabResourceName = "VibeGuard.Content.Models.vocab.txt";
    private const int EmbeddingDimension = 384;
    private const int MaxSequenceLength = 256;

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;

    private OnnxEmbeddingGenerator(InferenceSession session, BertTokenizer tokenizer)
    {
        _session = session;
        _tokenizer = tokenizer;
    }

    /// <summary>
    /// Creates a new generator by loading the ONNX model and vocabulary
    /// from the assembly's embedded resources.
    /// </summary>
    public static OnnxEmbeddingGenerator Create()
    {
        var assembly = typeof(OnnxEmbeddingGenerator).Assembly;

        using var modelStream = assembly.GetManifestResourceStream(ModelResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ModelResourceName}' not found.");
        using var ms = new MemoryStream();
        modelStream.CopyTo(ms);
        var session = new InferenceSession(ms.ToArray());

        using var vocabStream = assembly.GetManifestResourceStream(VocabResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{VocabResourceName}' not found.");
        var tokenizer = BertTokenizer.Create(vocabStream);

        return new OnnxEmbeddingGenerator(session, tokenizer);
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputs = values as IList<string> ?? values.ToList();
        var embeddings = new GeneratedEmbeddings<Embedding<float>>(inputs.Count);

        foreach (var text in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = Embed(text);
            embeddings.Add(new Embedding<float>(vector));
        }

        return Task.FromResult(embeddings);
    }

    private float[] Embed(string text)
    {
        var encoded = _tokenizer.EncodeToIds(text, MaxSequenceLength, out _, out _);

        var inputIds = new long[encoded.Count];
        var attentionMask = new long[encoded.Count];
        var tokenTypeIds = new long[encoded.Count];
        for (var i = 0; i < encoded.Count; i++)
        {
            inputIds[i] = encoded[i];
            attentionMask[i] = 1;
            tokenTypeIds[i] = 0;
        }

        var seqLen = encoded.Count;

        var onnxInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids",
                new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<long>(inputIds, [1, seqLen])),
            NamedOnnxValue.CreateFromTensor("attention_mask",
                new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<long>(attentionMask, [1, seqLen])),
            NamedOnnxValue.CreateFromTensor("token_type_ids",
                new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<long>(tokenTypeIds, [1, seqLen])),
        };

        using var results = _session.Run(onnxInputs);

        // The model outputs token-level embeddings of shape [1, seq_len, 384].
        // Mean-pool across the sequence dimension, then L2-normalize.
        var outputTensor = results[0].AsTensor<float>();
        var pooled = MeanPool(outputTensor, seqLen);
        L2Normalize(pooled);
        return pooled;
    }

    private static float[] MeanPool(Microsoft.ML.OnnxRuntime.Tensors.Tensor<float> tensor, int seqLen)
    {
        var result = new float[EmbeddingDimension];
        for (var t = 0; t < seqLen; t++)
        {
            for (var d = 0; d < EmbeddingDimension; d++)
            {
                result[d] += tensor[0, t, d];
            }
        }
        var divisor = (float)seqLen;
        for (var d = 0; d < EmbeddingDimension; d++)
        {
            result[d] /= divisor;
        }
        return result;
    }

    private static void L2Normalize(float[] vector)
    {
        var norm = 0f;
        for (var i = 0; i < vector.Length; i++)
            norm += vector[i] * vector[i];
        norm = MathF.Sqrt(norm);
        if (norm > 0f)
        {
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;
        }
    }

    // CA1822: Metadata is an interface property; it must remain an instance member.
#pragma warning disable CA1822
    public EmbeddingGeneratorMetadata Metadata => new("OnnxEmbeddingGenerator");
#pragma warning restore CA1822

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public TService? GetService<TService>(object? serviceKey = null) where TService : class
        => this as TService;

    public void Dispose() => _session.Dispose();
}
