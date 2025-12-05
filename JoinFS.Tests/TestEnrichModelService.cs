using JoinFS.DataModel;
using Newtonsoft.Json;
using System.Text;
using static JoinFS.Tests.Substitution;

namespace JoinFS.Helpers;

/// <summary>
/// Copy of EnrichModelService for testing purposes.
/// This version is independent of the main JoinFS application.
/// </summary>
public class EnrichModelService
{
    private const string ApiUrl = "https://joinfs.famtuduce.com/api/check-models";
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, EnrichedAircraftData> _modelDetails = new Dictionary<string, EnrichedAircraftData>();
    private readonly string _jsonlFilePath;

    public EnrichModelService(string jsonlFilePath, HttpClient? httpClient = null)
    {
        _jsonlFilePath = jsonlFilePath;
        _httpClient = httpClient ?? new HttpClient();
        LoadFromJsonlFile();
    }

    public IReadOnlyDictionary<string, EnrichedAircraftData> ModelDetails => _modelDetails;

    private void LoadFromJsonlFile()
    {
        if (!string.IsNullOrWhiteSpace(_jsonlFilePath) && File.Exists(_jsonlFilePath))
        {
            foreach (var line in File.ReadLines(_jsonlFilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    var data = JsonConvert.DeserializeObject<EnrichedAircraftData>(line);
                    if (data != null && !string.IsNullOrWhiteSpace(data.Title))
                    {
                        _modelDetails[data.Title] = data;
                    }
                }
                catch
                {
                    // Optionally log or handle malformed lines
                }
            }
        }
    }

    private void SaveToJsonlFile()
    {
        if (string.IsNullOrWhiteSpace(_jsonlFilePath))
            return;

        using (var writer = new StreamWriter(_jsonlFilePath, false, Encoding.UTF8))
        {
            foreach (var data in _modelDetails.Values)
            {
                var line = JsonConvert.SerializeObject(data);
                writer.WriteLine(line);
            }
        }
    }

    public async Task QueryAndStoreModelDetailsAsync(IEnumerable<string> modelTitles)
    {
        var missingTitles = modelTitles.Where(title => !_modelDetails.ContainsKey(title)).Distinct().ToList();
        if (missingTitles.Count == 0)
            return;

        var batches = Batch(missingTitles, 20);
        foreach (var batch in batches)
        {
            var request = new ModelCheckRequest { Models = batch.ToList() };
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using (var response = await _httpClient.PostAsync(ApiUrl, content))
            {
                response.EnsureSuccessStatusCode();
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<EnrichedModelResponse>(responseJson);

                if (result?.Data != null)
                {
                    foreach (var data in result.Data)
                    {
                        if (!string.IsNullOrWhiteSpace(data.Title))
                            _modelDetails[data.Title] = data;
                    }
                }
            }
        }

        SaveToJsonlFile();
    }

    private static IEnumerable<IEnumerable<T>> Batch<T>(IEnumerable<T> source, int size)
    {
        var batch = new List<T>(size);
        foreach (var item in source)
        {
            batch.Add(item);
            if (batch.Count == size)
            {
                yield return batch;
                batch = new List<T>(size);
            }
        }
        if (batch.Count > 0)
            yield return batch;
    }

    public async Task EnrichModelsWithDetailsAsync(IEnumerable<Model> models)
    {
        var uniqueTitles = models
            .Where(m => m != null && !string.IsNullOrWhiteSpace(m.title))
            .Select(m => m.title)
            .Distinct()
            .ToList();

        await QueryAndStoreModelDetailsAsync(uniqueTitles);

        foreach (var model in models)
        {
            if (model != null && model.title != null)
            {
                if (_modelDetails.TryGetValue(model.title, out var enriched))
                {
                    model.enrichedData = enriched;
                }
            }
        }
    }

    public void EnrichModel(Model? model)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.title))
            return;

        if (!_modelDetails.ContainsKey(model.title))
        {
            QueryAndStoreModelDetailsAsync(new[] { model.title }).Wait();
        }

        if (_modelDetails.TryGetValue(model.title, out var enriched))
        {
            model.enrichedData = enriched;
        }
    }
}
