using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using PLAVS.Application;

namespace PLAVS.Analysis;

public sealed class OnnxSentenceEncoder : ISemanticEncoder, IDisposable
{
    public const string Revision = "e8f8c211226b894fcb81acc59f3b34ba3efd5f42";
    public const string ModelId = "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2@" + Revision;
    public static string DefaultDirectory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("PLAVS_MODEL_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
            var cacheRoot = Environment.GetEnvironmentVariable("PLAVS_CACHE_DIRECTORY");
            if (string.IsNullOrWhiteSpace(cacheRoot))
                cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "plavs");
            return Path.Combine(cacheRoot, "models", Revision);
        }
    }
    private readonly InferenceSession? session;
    private readonly SentencePieceTokenizer? tokenizer;
    private readonly SemaphoreSlim inference = new(1, 1);
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 2048 });
    public SemanticStatus Status { get; }

    public OnnxSentenceEncoder(string directory)
    {
        var modelPath = Path.Combine(directory, "model.onnx");
        var tokenizerPath = Path.Combine(directory, "tokenizer.json");
        if (!File.Exists(modelPath) || !File.Exists(tokenizerPath))
        {
            Status = new(false, ModelId, "Локальну модель не встановлено. Виконайте scripts/download-model.py та перезапустіть API."); return;
        }
        try
        {
            Verify(modelPath, "10f7a088420252b26caf819236ca2c9d2987afd0fc06fec7553b542a5655a05a");
            Verify(tokenizerPath, "2c3387be76557bd40970cec13153b3bbf80407865484b209e655e5e4729076b8");
            using var stream = File.OpenRead(tokenizerPath);
            tokenizer = SentencePieceTokenizer.CreateFromTokenizerJson(stream, addBeginningOfSentence: true, addEndOfSentence: true);
            using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            session = new InferenceSession(modelPath, options);
            if (!session.InputMetadata.ContainsKey("input_ids") || !session.InputMetadata.ContainsKey("attention_mask"))
                throw new InvalidOperationException("Несумісні вхідні тензори моделі.");
            Status = new(true, ModelId, "Локальна ONNX-модель готова. Відповіді не передаються зовнішнім сервісам.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OnnxRuntimeException or NotSupportedException or ArgumentException)
        {
            session?.Dispose(); session = null; tokenizer = null;
            Status = new(false, ModelId, "Не вдалося перевірити або завантажити локальну модель. Повторіть завантаження й перевірте сумісність ONNX Runtime.");
        }
    }
    private static void Verify(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Контрольна сума моделі не збігається з маніфестом.");
    }
    public int[] TokenIds(string text)
    {
        if (tokenizer is null) throw new InvalidOperationException(Status.Message);
        return tokenizer.EncodeToIds(text).ToArray();
    }
    public async Task<Embedding> EncodeAsync(string text, bool cache, CancellationToken ct = default)
    {
        if (session is null || tokenizer is null) throw new InvalidOperationException(Status.Message);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ModelId + "\n" + text)));
        if (cache && this.cache.TryGetValue<Embedding>(key, out var cached)) return cached!;
        await inference.WaitAsync(ct);
        try
        {
            if (cache && this.cache.TryGetValue<Embedding>(key, out cached)) return cached!;
            var tokens = TokenIds(text);
            var truncated = tokens.Length > 128;
            if (truncated) tokens = tokens.Take(127).Append(tokens[^1]).ToArray();
            if (tokens.Length < 2 || tokens[0] != 0 || tokens[^1] != 2) throw new InvalidOperationException("Несумісні спеціальні токени: очікуються <s>=0 і </s>=2.");
            var length = tokens.Length;
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(tokens.Select(t => (long)t).ToArray(), new[] { 1, length })),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(Enumerable.Repeat(1L, length).ToArray(), new[] { 1, length }))
            };
            if (session.InputMetadata.ContainsKey("token_type_ids"))
                inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[length], new[] { 1, length })));
            ct.ThrowIfCancellationRequested();
            using var outputs = session.Run(inputs);
            var tensor = outputs.First(o => o.AsTensor<float>().Rank == 3).AsTensor<float>();
            if (tensor.Dimensions[0] != 1 || tensor.Dimensions[1] != length || tensor.Dimensions[2] != 384)
                throw new InvalidOperationException("Несумісна розмірність embedding-тензора.");
            var vector = new float[384];
            // No padding is emitted for single-sequence inference, so every attention-mask element is one.
            for (var t = 0; t < length; t++)
                for (var d = 0; d < 384; d++) vector[d] += tensor[0, t, d] / length;
            var norm = Math.Sqrt(vector.Sum(x => (double)x * x));
            if (norm <= 1e-12 || !double.IsFinite(norm)) throw new InvalidOperationException("Модель повернула некоректний embedding-вектор.");
            for (var d = 0; d < vector.Length; d++) vector[d] = (float)(vector[d] / norm);
            var result = new Embedding(vector, truncated);
            if (cache) this.cache.Set(key, result, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromHours(8) });
            return result;
        }
        finally { inference.Release(); }
    }
    public void Dispose() { session?.Dispose(); cache.Dispose(); inference.Dispose(); }
}
