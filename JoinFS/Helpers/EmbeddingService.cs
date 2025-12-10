#if X64
using FastBertTokenizer;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static JoinFS.Substitution;

public class EmbeddingService : IDisposable
{
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;

    public EmbeddingService(string modelPath, string vocabPath)
    {
        // Load the ONNX model
        _session = new InferenceSession(modelPath);
        _tokenizer = new BertTokenizer();
        _tokenizer.LoadVocabularyAsync(vocabPath, true).GetAwaiter().GetResult();

    }

    public float[] GenerateEmbedding(string text)
    {
        // 1. Tokenize using FastBertTokenizer
        // This returns the exact arrays needed: InputIds, AttentionMask, TokenTypeIds
        var (inputIds, attentionMask, tokenTypeIds) = _tokenizer.Encode(text);

        // 2. Create Tensors
        // We cast to long[] because OnnxRuntime expects Int64 (long) for indices
        var inputIdsLong = Array.ConvertAll(inputIds.ToArray(), x => (long)x);
        var maskLong = Array.ConvertAll(attentionMask.ToArray(), x => (long)x);
        var typesLong = Array.ConvertAll(tokenTypeIds.ToArray(), x => (long)x);

        // Create DenseTensors with shape [1, SequenceLength]
        var dimensions = new[] { 1, inputIdsLong.Length };

        var inputIdsTensor = new DenseTensor<long>(inputIdsLong, dimensions);
        var attentionMaskTensor = new DenseTensor<long>(maskLong, dimensions);
        var tokenTypeIdsTensor = new DenseTensor<long>(typesLong, dimensions);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
        };

        // 3. Run Inference
        using (var results = _session.Run(inputs))
        {
            // 4. Extract Output
            // The output name for this model is usually "last_hidden_state" or "model_outputs"
            // We use First() to get the first output regardless of name to be safe
            var lastHiddenState = results.First().AsTensor<float>();

            // 5. Mean Pooling
            var embedding = MeanPooling(lastHiddenState, maskLong);

            // 6. Normalize
            return Normalize(embedding);
        }
    }

    public async Task GenerateEmbeddingsFromModelsAsync(IEnumerable<Model> models)
    {
        await Task.Run(() => GenerateEmbeddingsFromModels(models));
    }

    public void GenerateEmbeddingsFromModels(IEnumerable<Model> models)
    {
        var embeddings = new List<float[]>();
        foreach (var model in models)
        {
            string text = null;
            if (model.enrichedData != null)
            {
                text =  $"title: {model.title} {model.variation}, " +
                        $"role: {model.enrichedData.TypeRole}, " +
                        $"wing position: {model.enrichedData.WingPosition}, " +
                        $"number engines: {model.enrichedData.Engines}, " +
                        $"engine type: {model.enrichedData.EngineType}, " +
                        $"wake: {model.enrichedData.Wake}, " +
                        $"military: {model.enrichedData.Military}".Trim();

            }
            else
            {
                text =  $"title: {model.title} {model.variation}, " +
                        $"role: {model.typerole}".Trim();
            }
            model.embedding = GenerateEmbedding(text);
        }
    }

    private float[] MeanPooling(Tensor<float> lastHiddenState, long[] attentionMask)
    {
        // Shape: [BatchSize, SequenceLength, HiddenSize]
        // BatchSize is 1
        int sequenceLength = lastHiddenState.Dimensions[1];
        int hiddenSize = lastHiddenState.Dimensions[2];

        var pooledEmbedding = new float[hiddenSize];
        int validTokenCount = 0;

        for (int i = 0; i < sequenceLength; i++)
        {
            if (attentionMask[i] == 1) // Only sum valid tokens (not padding)
            {
                validTokenCount++;
                for (int j = 0; j < hiddenSize; j++)
                {
                    pooledEmbedding[j] += lastHiddenState[0, i, j];
                }
            }
        }

        // Average
        for (int i = 0; i < hiddenSize; i++)
        {
            pooledEmbedding[i] /= validTokenCount;
        }

        return pooledEmbedding;
    }

    private float[] Normalize(float[] vector)
    {
        float sumSquares = vector.Sum(v => v * v);
        float norm = (float)Math.Sqrt(sumSquares);
        return vector.Select(v => v / norm).ToArray();
    }

    public Model FindBestMatchingModel(Model matchModel, IEnumerable<Model> models, float threshold)
    {
        if (matchModel.embedding == null)
            matchModel.embedding = GenerateEmbedding(
                matchModel.enrichedData != null
                    ? $"title: {matchModel.title} {matchModel.variation}, role: {matchModel.enrichedData.TypeRole}, wing position: {matchModel.enrichedData.WingPosition}, number engines: {matchModel.enrichedData.Engines}, engine type: {matchModel.enrichedData.EngineType}, wake: {matchModel.enrichedData.Wake}, military: {matchModel.enrichedData.Military}".Trim()
                    : $"title: {matchModel.title} {matchModel.variation}, role: {matchModel.typerole}".Trim()
            );

        Model bestModel = null;
        float bestScore = threshold;

        foreach (var model in models)
        {
            if (model.embedding == null)
                continue;

            float score = CosineSimilarity(matchModel.embedding, model.embedding);
            if (score > bestScore)
            {
                bestScore = score;
                bestModel = model;
            }
        }

        return bestModel;
    }

    private float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must be the same length");

        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return dot / ((float)Math.Sqrt(normA) * (float)Math.Sqrt(normB));
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}

#endif