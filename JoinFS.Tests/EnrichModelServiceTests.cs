using JoinFS.DataModel;
using JoinFS.Helpers;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using Xunit;
using static JoinFS.Tests.Substitution;

namespace JoinFS.Tests;

/// <summary>
/// Tests for the EnrichModelService class which handles enrichment of aircraft model data
/// through API calls and local caching.
/// </summary>
public class EnrichModelServiceTests : IDisposable
{
    private readonly string _testFilePath;

    public EnrichModelServiceTests()
    {
        // Create a unique temp file for each test
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test_models_{Guid.NewGuid()}.jsonl");
    }

    public void Dispose()
    {
        // Clean up test file
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidPath_InitializesEmpty()
    {
        // Arrange & Act
        var service = new EnrichModelService(_testFilePath);

        // Assert
        Assert.Empty(service.ModelDetails);
    }

    [Fact]
    public void Constructor_WithNonExistentFile_InitializesEmpty()
    {
        // Arrange
        string nonExistentPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.jsonl");

        // Act
        var service = new EnrichModelService(nonExistentPath);

        // Assert
        Assert.Empty(service.ModelDetails);
    }

    [Fact]
    public void Constructor_LoadsExistingData()
    {
        // Arrange
        var testData = new EnrichedAircraftData
        {
            Title = "Boeing 737-800",
            TypeRole = "Airliner",
            WingPosition = "Low",
            Engines = 2,
            EngineType = "Jet",
            Wake = "Medium",
            Military = false
        };
        File.WriteAllText(_testFilePath, JsonConvert.SerializeObject(testData));

        // Act
        var service = new EnrichModelService(_testFilePath);

        // Assert
        Assert.Single(service.ModelDetails);
        Assert.True(service.ModelDetails.ContainsKey("Boeing 737-800"));
        Assert.Equal("Airliner", service.ModelDetails["Boeing 737-800"].TypeRole);
    }

    [Fact]
    public void Constructor_WithEmptyFile_InitializesEmpty()
    {
        // Arrange
        File.WriteAllText(_testFilePath, string.Empty);

        // Act
        var service = new EnrichModelService(_testFilePath);

        // Assert
        Assert.Empty(service.ModelDetails);
    }

    [Fact]
    public void Constructor_IgnoresMalformedLines()
    {
        // Arrange
        var validData = new EnrichedAircraftData { Title = "Valid Aircraft", TypeRole = "Airliner", Engines = 2 };
        var lines = new[]
        {
            JsonConvert.SerializeObject(validData),
            "{ invalid json",
            "",
            JsonConvert.SerializeObject(new EnrichedAircraftData { Title = "Another Aircraft", TypeRole = "Cargo", Engines = 4 })
        };
        File.WriteAllLines(_testFilePath, lines);

        // Act
        var service = new EnrichModelService(_testFilePath);

        // Assert
        Assert.Equal(2, service.ModelDetails.Count);
        Assert.True(service.ModelDetails.ContainsKey("Valid Aircraft"));
        Assert.True(service.ModelDetails.ContainsKey("Another Aircraft"));
    }

    [Fact]
    public void Constructor_WithNullTitle_SkipsEntry()
    {
        // Arrange
        var dataWithNullTitle = new { Title = (string?)null, TypeRole = "Unknown" };
        File.WriteAllText(_testFilePath, JsonConvert.SerializeObject(dataWithNullTitle));

        // Act
        var service = new EnrichModelService(_testFilePath);

        // Assert
        Assert.Empty(service.ModelDetails);
    }

    #endregion

    #region QueryAndStoreModelDetailsAsync Tests

    [Fact]
    public async Task QueryAndStoreModelDetailsAsync_WithEmptyList_DoesNotCallApi()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        var service = new EnrichModelService(_testFilePath, httpClient);

        // Act
        await service.QueryAndStoreModelDetailsAsync(new List<string>());

        // Assert
        mockHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task QueryAndStoreModelDetailsAsync_SkipsAlreadyLoadedModels()
    {
        // Arrange
        var existingData = new EnrichedAircraftData { Title = "Existing Model", TypeRole = "Fighter", Engines = 2 };
        File.WriteAllText(_testFilePath, JsonConvert.SerializeObject(existingData));

        var mockHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHandler.Object);
        var service = new EnrichModelService(_testFilePath, httpClient);

        // Act
        await service.QueryAndStoreModelDetailsAsync(new[] { "Existing Model" });

        // Assert - Should not call API since model already exists
        mockHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task QueryAndStoreModelDetailsAsync_CallsApiForNewModels()
    {
        // Arrange
        var responseData = new EnrichedModelResponse
        {
            FoundCount = 1,
            BufferedCount = 0,
            Data = new List<EnrichedAircraftData>
            {
                new EnrichedAircraftData
                {
                    Title = "Cessna 172",
                    TypeRole = "General Aviation",
                    WingPosition = "High",
                    Engines = 1,
                    EngineType = "Piston",
                    Wake = "Light",
                    Military = false
                }
            }
        };

        var mockHandler = CreateMockHttpHandler(responseData);
        var httpClient = new HttpClient(mockHandler.Object);
        var service = new EnrichModelService(_testFilePath, httpClient);

        // Act
        await service.QueryAndStoreModelDetailsAsync(new[] { "Cessna 172" });

        // Assert
        Assert.Single(service.ModelDetails);
        Assert.True(service.ModelDetails.ContainsKey("Cessna 172"));
        Assert.Equal("General Aviation", service.ModelDetails["Cessna 172"].TypeRole);
    }

    [Fact]
    public async Task QueryAndStoreModelDetailsAsync_BatchesRequests()
    {
        // Arrange
        var models = Enumerable.Range(1, 25).Select(i => $"Model{i}").ToList();
        var responseData = new EnrichedModelResponse
        {
            FoundCount = 20,
            BufferedCount = 0,
            Data = models.Take(20).Select(m => new EnrichedAircraftData { Title = m, TypeRole = "Test", Engines = 1 }).ToList()
        };

        var mockHandler = CreateMockHttpHandler(responseData);
        var httpClient = new HttpClient(mockHandler.Object);
        var service = new EnrichModelService(_testFilePath, httpClient);

        // Act
        await service.QueryAndStoreModelDetailsAsync(models);

        // Assert - Should make 2 API calls (20 + 5 models)
        mockHandler.Protected().Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task QueryAndStoreModelDetailsAsync_SavesDataToFile()
    {
        // Arrange
        var responseData = new EnrichedModelResponse
        {
            FoundCount = 1,
            BufferedCount = 0,
            Data = new List<EnrichedAircraftData>
            {
                new EnrichedAircraftData { Title = "Test Aircraft", TypeRole = "Bomber", Engines = 4 }
            }
        };

        var mockHandler = CreateMockHttpHandler(responseData);
        var httpClient = new HttpClient(mockHandler.Object);
        var service = new EnrichModelService(_testFilePath, httpClient);

        // Act
        await service.QueryAndStoreModelDetailsAsync(new[] { "Test Aircraft" });

        // Assert
        Assert.True(File.Exists(_testFilePath));
        var lines = File.ReadAllLines(_testFilePath);
        Assert.Single(lines);
        var savedData = JsonConvert.DeserializeObject<EnrichedAircraftData>(lines[0]);
        Assert.NotNull(savedData);
        Assert.Equal("Test Aircraft", savedData.Title);
    }

    [Fact]
    public async Task QueryAndStoreModelDetailsAsync_HandlesDuplicateTitles()
    {
        // Arrange
        var responseData = new EnrichedModelResponse
        {
            FoundCount = 1,
            BufferedCount = 0,
            Data = new List<EnrichedAircraftData>
            {
                new EnrichedAircraftData { Title = "Duplicate", TypeRole = "Fighter", Engines = 1 }
            }
        };

        var mockHandler = CreateMockHttpHandler(responseData);
        var httpClient = new HttpClient(mockHandler.Object);
        var service = new EnrichModelService(_testFilePath, httpClient);

        // Act
        await service.QueryAndStoreModelDetailsAsync(new[] { "Duplicate", "Duplicate", "Duplicate" });

        // Assert - Should only query once and store once
        Assert.Single(service.ModelDetails);
        mockHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    #endregion

    #region EnrichModelsWithDetailsAsync Tests

    [Fact]
    public async Task EnrichModelsWithDetailsAsync_EnrichesAllModels()
    {
        // Arrange
        var responseData = new EnrichedModelResponse
        {
            FoundCount = 2,
            BufferedCount = 0,
            Data = new List<EnrichedAircraftData>
            {
                new EnrichedAircraftData { Title = "Model1", TypeRole = "Fighter", Engines = 1 },
                new EnrichedAircraftData { Title = "Model2", TypeRole = "Bomber", Engines = 2 }
            }
        };

        var mockHandler = CreateMockHttpHandler(responseData);
        var httpClient = new HttpClient(mockHandler.Object);
        var service = new EnrichModelService(_testFilePath, httpClient);

        var models = new[]
        {
            CreateTestModel("Model1", "Variation1"),
            CreateTestModel("Model2", "Variation2")
        };

        // Act
        await service.EnrichModelsWithDetailsAsync(models);

        // Assert
        Assert.NotNull(models[0].enrichedData);
        Assert.Equal("Fighter", models[0].enrichedData.TypeRole);
        Assert.NotNull(models[1].enrichedData);
        Assert.Equal("Bomber", models[1].enrichedData.TypeRole);
    }

    [Fact]
    public async Task EnrichModelsWithDetailsAsync_HandlesNullModels()
    {
        // Arrange
        var responseData = new EnrichedModelResponse
        {
            FoundCount = 1,
            BufferedCount = 0,
            Data = new List<EnrichedAircraftData>
            {
                new EnrichedAircraftData { Title = "Test", TypeRole = "Transport", Engines = 2 }
            }
        };

        var mockHandler = CreateMockHttpHandler(responseData);
        var httpClient = new HttpClient(mockHandler.Object);
        var service = new EnrichModelService(_testFilePath, httpClient);
        var models = new[] { (Substitution.Model?)null, CreateTestModel("Test", "Var") };

        // Act & Assert - Should not throw
        await service.EnrichModelsWithDetailsAsync(models!);
    }

    [Fact]
    public async Task EnrichModelsWithDetailsAsync_HandlesEmptyTitles()
    {
        // Arrange
        var service = new EnrichModelService(_testFilePath);
        var models = new[]
        {
            CreateTestModel("", "Variation"),
            CreateTestModel("  ", "Variation")
        };

        // Act & Assert - Should not throw
        await service.EnrichModelsWithDetailsAsync(models);
    }

    [Fact]
    public async Task EnrichModelsWithDetailsAsync_UsesDistinctTitles()
    {
        // Arrange
        var responseData = new EnrichedModelResponse
        {
            FoundCount = 1,
            BufferedCount = 0,
            Data = new List<EnrichedAircraftData>
            {
                new EnrichedAircraftData { Title = "SameTitle", TypeRole = "Transport", Engines = 2 }
            }
        };

        var mockHandler = CreateMockHttpHandler(responseData);
        var httpClient = new HttpClient(mockHandler.Object);
        var service = new EnrichModelService(_testFilePath, httpClient);

        var models = new[]
        {
            CreateTestModel("SameTitle", "Var1"),
            CreateTestModel("SameTitle", "Var2"),
            CreateTestModel("SameTitle", "Var3")
        };

        // Act
        await service.EnrichModelsWithDetailsAsync(models);

        // Assert - Should only call API once for the unique title
        mockHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
        Assert.All(models, m => Assert.NotNull(m.enrichedData));
    }

    #endregion

    #region EnrichModel Tests

    [Fact]
    public void EnrichModel_WithNullModel_DoesNotThrow()
    {
        // Arrange
        var service = new EnrichModelService(_testFilePath);

        // Act & Assert
        service.EnrichModel(null!);
    }

    [Fact]
    public void EnrichModel_WithEmptyTitle_DoesNotThrow()
    {
        // Arrange
        var service = new EnrichModelService(_testFilePath);
        var model = CreateTestModel("", "Variation");

        // Act & Assert
        service.EnrichModel(model);
        Assert.Null(model.enrichedData);
    }

    [Fact]
    public void EnrichModel_WithExistingData_EnrichesModel()
    {
        // Arrange
        var existingData = new EnrichedAircraftData
        {
            Title = "Existing",
            TypeRole = "Helicopter",
            Engines = 2
        };
        File.WriteAllText(_testFilePath, JsonConvert.SerializeObject(existingData));
        var service = new EnrichModelService(_testFilePath);
        var model = CreateTestModel("Existing", "Military");

        // Act
        service.EnrichModel(model);

        // Assert
        Assert.NotNull(model.enrichedData);
        Assert.Equal("Helicopter", model.enrichedData.TypeRole);
        Assert.Equal(2, model.enrichedData.Engines);
    }

    #endregion

    #region Helper Methods

    private Mock<HttpMessageHandler> CreateMockHttpHandler(EnrichedModelResponse responseData)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonConvert.SerializeObject(responseData), Encoding.UTF8, "application/json")
            });
        return mockHandler;
    }

    private Substitution.Model CreateTestModel(string title, string variation)
    {
        return new Substitution.Model(
            title: title,
            manufacturer: "Test Manufacturer",
            type: "Test Type",
            variation: variation,
            index: 0,
            typerole: "0",
            smoke: "0",
            folder: "TestFolder"
        );
    }

    #endregion
}
