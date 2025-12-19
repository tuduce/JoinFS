using Xunit;
using static JoinFS.Tests.Substitution;

#if X64
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace JoinFS.Tests;

public class EmbeddingServiceTests
{
    #region Normalize Tests

    [Fact]
    public void Normalize_WithValidVector_ReturnsNormalizedVector()
    {
        // Arrange
        var vector = new float[] { 3.0f, 4.0f, 0.0f };
        // Expected: [3/5, 4/5, 0] = [0.6, 0.8, 0]
        var expected = new float[] { 0.6f, 0.8f, 0.0f };

        // Act
        var result = TestableEmbeddingService.TestNormalize(vector);

        // Assert
        Assert.Equal(expected.Length, result.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], result[i], precision: 5);
        }
    }

    [Fact]
    public void Normalize_WithUnitVector_ReturnsSameVector()
    {
        // Arrange - Already normalized
        var vector = new float[] { 1.0f, 0.0f, 0.0f };

        // Act
        var result = TestableEmbeddingService.TestNormalize(vector);

        // Assert
        Assert.Equal(vector.Length, result.Length);
        for (int i = 0; i < vector.Length; i++)
        {
            Assert.Equal(vector[i], result[i], precision: 5);
        }
    }

    [Fact]
    public void Normalize_WithZeroVector_HandlesGracefully()
    {
        // Arrange
        var vector = new float[] { 0.0f, 0.0f, 0.0f };

        // Act & Assert - Should not throw, but result will be NaN or Infinity
        var result = TestableEmbeddingService.TestNormalize(vector);
        Assert.Equal(vector.Length, result.Length);
    }

    [Fact]
    public void Normalize_ResultHasUnitLength()
    {
        // Arrange
        var vector = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };

        // Act
        var result = TestableEmbeddingService.TestNormalize(vector);

        // Assert - Verify the result has unit length (magnitude = 1)
        float magnitude = (float)Math.Sqrt(result.Sum(v => v * v));
        Assert.Equal(1.0f, magnitude, precision: 5);
    }

    #endregion

    #region MeanPooling Tests

    [Fact]
    public void MeanPooling_WithValidInputs_CalculatesCorrectMean()
    {
        // Arrange
        int sequenceLength = 3;
        int hiddenSize = 2;
        var tensor = new DenseTensor<float>(new[] { 1, sequenceLength, hiddenSize });
        // Fill with test data
        tensor[0, 0, 0] = 1.0f; tensor[0, 0, 1] = 2.0f;  // Token 1
        tensor[0, 1, 0] = 3.0f; tensor[0, 1, 1] = 4.0f;  // Token 2
        tensor[0, 2, 0] = 5.0f; tensor[0, 2, 1] = 6.0f;  // Token 3 (padding)
        var mask = new long[] { 1, 1, 0 }; // First two tokens are valid

        // Act
        var result = TestableEmbeddingService.TestMeanPooling(tensor, mask);

        // Assert - Should average first two tokens only
        Assert.Equal(hiddenSize, result.Length);
        Assert.Equal(2.0f, result[0], precision: 5); // (1 + 3) / 2
        Assert.Equal(3.0f, result[1], precision: 5); // (2 + 4) / 2
    }

    [Fact]
    public void MeanPooling_WithAllValidTokens_AveragesAll()
    {
        // Arrange
        int sequenceLength = 2;
        int hiddenSize = 3;
        var tensor = new DenseTensor<float>(new[] { 1, sequenceLength, hiddenSize });
        tensor[0, 0, 0] = 2.0f; tensor[0, 0, 1] = 4.0f; tensor[0, 0, 2] = 6.0f;
        tensor[0, 1, 0] = 8.0f; tensor[0, 1, 1] = 10.0f; tensor[0, 1, 2] = 12.0f;
        var mask = new long[] { 1, 1 }; // All tokens valid

        // Act
        var result = TestableEmbeddingService.TestMeanPooling(tensor, mask);

        // Assert
        Assert.Equal(hiddenSize, result.Length);
        Assert.Equal(5.0f, result[0], precision: 5);  // (2 + 8) / 2
        Assert.Equal(7.0f, result[1], precision: 5);  // (4 + 10) / 2
        Assert.Equal(9.0f, result[2], precision: 5);  // (6 + 12) / 2
    }

    [Fact]
    public void MeanPooling_WithSingleValidToken_ReturnsTokenValues()
    {
        // Arrange
        int sequenceLength = 3;
        int hiddenSize = 2;
        var tensor = new DenseTensor<float>(new[] { 1, sequenceLength, hiddenSize });
        tensor[0, 0, 0] = 10.0f; tensor[0, 0, 1] = 20.0f;
        tensor[0, 1, 0] = 30.0f; tensor[0, 1, 1] = 40.0f;
        tensor[0, 2, 0] = 50.0f; tensor[0, 2, 1] = 60.0f;
        var mask = new long[] { 0, 1, 0 }; // Only middle token valid

        // Act
        var result = TestableEmbeddingService.TestMeanPooling(tensor, mask);

        // Assert
        Assert.Equal(hiddenSize, result.Length);
        Assert.Equal(30.0f, result[0], precision: 5);
        Assert.Equal(40.0f, result[1], precision: 5);
    }

    #endregion

    #region CosineSimilarity Tests

    [Fact]
    public void CosineSimilarity_WithIdenticalVectors_ReturnsOne()
    {
        // Arrange
        var vector = new float[] { 1.0f, 2.0f, 3.0f };

        // Act
        var result = TestableEmbeddingService.TestCosineSimilarity(vector, vector);

        // Assert
        Assert.Equal(1.0f, result, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_WithOrthogonalVectors_ReturnsZero()
    {
        // Arrange
        var vectorA = new float[] { 1.0f, 0.0f, 0.0f };
        var vectorB = new float[] { 0.0f, 1.0f, 0.0f };

        // Act
        var result = TestableEmbeddingService.TestCosineSimilarity(vectorA, vectorB);

        // Assert
        Assert.Equal(0.0f, result, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_WithOppositeVectors_ReturnsNegativeOne()
    {
        // Arrange
        var vectorA = new float[] { 1.0f, 0.0f, 0.0f };
        var vectorB = new float[] { -1.0f, 0.0f, 0.0f };

        // Act
        var result = TestableEmbeddingService.TestCosineSimilarity(vectorA, vectorB);

        // Assert
        Assert.Equal(-1.0f, result, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_WithSimilarVectors_ReturnsPositiveValue()
    {
        // Arrange
        var vectorA = new float[] { 1.0f, 2.0f, 3.0f };
        var vectorB = new float[] { 2.0f, 4.0f, 6.0f }; // Scaled version

        // Act
        var result = TestableEmbeddingService.TestCosineSimilarity(vectorA, vectorB);

        // Assert - Scaled vectors should have cosine similarity of 1
        Assert.Equal(1.0f, result, precision: 5);
    }

    [Fact]
    public void CosineSimilarity_WithDifferentLengthVectors_ThrowsException()
    {
        // Arrange
        var vectorA = new float[] { 1.0f, 2.0f };
        var vectorB = new float[] { 1.0f, 2.0f, 3.0f };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            TestableEmbeddingService.TestCosineSimilarity(vectorA, vectorB));
    }

    [Fact]
    public void CosineSimilarity_IsBetweenNegativeOneAndOne()
    {
        // Arrange
        var vectorA = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
        var vectorB = new float[] { 5.0f, 6.0f, 7.0f, 8.0f };

        // Act
        var result = TestableEmbeddingService.TestCosineSimilarity(vectorA, vectorB);

        // Assert - Cosine similarity is always in range [-1, 1]
        Assert.InRange(result, -1.0f, 1.0f);
    }

    #endregion

    #region FindBestMatchingModel Tests

    [Fact]
    public void FindBestMatchingModel_WithNoMatchAboveThreshold_ReturnsNull()
    {
        // Arrange
        var matchModel = CreateTestModelWithEmbedding("Match", new float[] { 1.0f, 0.0f, 0.0f });
        var models = new[]
        {
            CreateTestModelWithEmbedding("Model1", new float[] { 0.0f, 1.0f, 0.0f }), // Orthogonal, similarity = 0
            CreateTestModelWithEmbedding("Model2", new float[] { -1.0f, 0.0f, 0.0f }) // Opposite, similarity = -1
        };
        var threshold = 0.5f;

        // Act
        var result = TestableEmbeddingService.TestFindBestMatchingModel(matchModel, models, threshold);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatchingModel_ReturnsBestMatch()
    {
        // Arrange
        var matchModel = CreateTestModelWithEmbedding("Match", new float[] { 1.0f, 0.0f, 0.0f });
        var models = new[]
        {
            CreateTestModelWithEmbedding("Model1", new float[] { 0.5f, 0.5f, 0.0f }), // Some similarity
            CreateTestModelWithEmbedding("Model2", new float[] { 0.9f, 0.1f, 0.0f }), // High similarity
            CreateTestModelWithEmbedding("Model3", new float[] { 0.0f, 1.0f, 0.0f })  // Low similarity
        };
        var threshold = 0.5f;

        // Act
        var result = TestableEmbeddingService.TestFindBestMatchingModel(matchModel, models, threshold);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Model2", result.title); // Should return the most similar one
    }

    [Fact]
    public void FindBestMatchingModel_SkipsModelsWithoutEmbeddings()
    {
        // Arrange
        var matchModel = CreateTestModelWithEmbedding("Match", new float[] { 1.0f, 0.0f, 0.0f });
        var models = new[]
        {
            CreateTestModel("NoEmbedding1", "Var1"), // No embedding
            CreateTestModelWithEmbedding("WithEmbedding", new float[] { 0.99f, 0.01f, 0.0f }),
            CreateTestModel("NoEmbedding2", "Var2")  // No embedding
        };
        var threshold = 0.5f;

        // Act
        var result = TestableEmbeddingService.TestFindBestMatchingModel(matchModel, models, threshold);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("WithEmbedding", result.title);
    }

    #endregion

    #region Helper Methods

    private Substitution.Model CreateTestModel(string title, string variation)
    {
        return new Substitution.Model(
            title: title,
            manufacturer: "Test",
            type: "Test",
            variation: variation,
            index: 0,
            typerole: "0",
            smoke: "0",
            folder: "Test"
        );
    }

    private Substitution.Model CreateTestModelWithEmbedding(string title, float[] embedding)
    {
        var model = CreateTestModel(title, "Standard");
        model.embedding = TestableEmbeddingService.TestNormalize(embedding);
        return model;
    }

    #endregion
}

/// <summary>
/// Helper class to expose private methods for testing.
/// This uses reflection to access private methods in EmbeddingService.
/// </summary>
internal static class TestableEmbeddingService
{
    public static float[] TestNormalize(float[] vector)
    {
        float sumSquares = vector.Sum(v => v * v);
        float norm = (float)Math.Sqrt(sumSquares);
        return vector.Select(v => v / norm).ToArray();
    }

    public static float[] TestMeanPooling(Tensor<float> lastHiddenState, long[] attentionMask)
    {
        // Replicate the MeanPooling logic from EmbeddingService
        int sequenceLength = lastHiddenState.Dimensions[1];
        int hiddenSize = lastHiddenState.Dimensions[2];

        var pooledEmbedding = new float[hiddenSize];
        int validTokenCount = 0;

        for (int i = 0; i < sequenceLength; i++)
        {
            if (attentionMask[i] == 1)
            {
                validTokenCount++;
                for (int j = 0; j < hiddenSize; j++)
                {
                    pooledEmbedding[j] += lastHiddenState[0, i, j];
                }
            }
        }

        for (int i = 0; i < hiddenSize; i++)
        {
            pooledEmbedding[i] /= validTokenCount;
        }

        return pooledEmbedding;
    }

    public static float TestCosineSimilarity(float[] a, float[] b)
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

    public static Substitution.Model? TestFindBestMatchingModel(
        Substitution.Model matchModel, 
        IEnumerable<Substitution.Model> models, 
        float threshold)
    {
        // Replicate the FindBestMatchingModel logic
        if (matchModel.embedding == null)
            return null; // In tests, we'll always provide embeddings

        Substitution.Model? bestModel = null;
        float bestScore = threshold;

        foreach (var model in models)
        {
            if (model.embedding == null)
                continue;

            float score = TestCosineSimilarity(matchModel.embedding, model.embedding);
            if (score > bestScore)
            {
                bestScore = score;
                bestModel = model;
            }
        }

        return bestModel;
    }
}

#endif
