using JoinFS.DataModel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using static JoinFS.Substitution;

namespace JoinFS.Helpers
{
    public class EnrichModelService
    {
        // TODO: Move to configuration
        private const string ApiUrl = "https://joinfs.famtuduce.com/api/check-models";
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, EnrichedAircraftData> _modelDetails = [];
        private readonly string _jsonlFilePath;
        private readonly Main _main;

        public EnrichModelService(string jsonlFilePath, HttpClient httpClient = null, Main main = null)
        {
            _jsonlFilePath = jsonlFilePath;
            _httpClient = httpClient ?? new HttpClient();
            _main = main;
            LoadFromJsonlFile();
        }

        public IReadOnlyDictionary<string, EnrichedAircraftData> ModelDetails => _modelDetails;
        private readonly object _fileLock = new();

        private void LoadFromJsonlFile()
        {
            if (!string.IsNullOrWhiteSpace(_jsonlFilePath) && File.Exists(_jsonlFilePath))
            {
                lock (_fileLock)
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
                            // TODO: Optionally log or handle malformed lines
                        }
                    }
                }
            }
        }

        private void SaveToJsonlFile()
        {
            if (string.IsNullOrWhiteSpace(_jsonlFilePath))
                return;

            var tempPath = _jsonlFilePath + ".tmp";
            lock (_fileLock)
            {
                // Write to a temp file first
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    // Write all unique model details to the file, one per line
                    using var writer = new StreamWriter(fs, Encoding.UTF8);
                    foreach (var data in _modelDetails.Values)
                    {
                        var line = JsonConvert.SerializeObject(data);
                        writer.WriteLine(line);
                    }
                }
                // Atomically replace
                File.Copy(tempPath, _jsonlFilePath, overwrite: true);
                File.Delete(tempPath);
            }
        }

        public async Task QueryAndStoreModelDetailsAsync(IEnumerable<string> modelTitles)
        {
            // Only query models not already in the dictionary
            var missingTitles = modelTitles.Where(title => !_modelDetails.ContainsKey(title)).Distinct().ToList();
            if (missingTitles.Count == 0)
                return;

            var batches = Batch(missingTitles, 20);
            foreach (var batch in batches)
            {
                try
                {
                    var request = new ModelCheckRequest { Models = [.. batch] };
                    var json = JsonConvert.SerializeObject(request);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var response = await _httpClient.PostAsync(ApiUrl, content); //.ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    var responseJson = await response.Content.ReadAsStringAsync(); //.ConfigureAwait(false);
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
                catch (HttpRequestException ex)
                {
                    _main?.MonitorEvent($"Error enriching model data: {ex.Message}");
                }
                catch (TaskCanceledException ex)
                {
                    _main?.MonitorEvent($"Request timeout enriching model data: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _main?.MonitorEvent($"Unexpected error enriching model data: {ex.Message}");
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

        /// <summary>
        /// Enriches the given list of models with data from the API and assigns the enrichment to each model.
        /// </summary>
        /// <param name="models">A collection of Model objects to enrich.</param>
        public async Task EnrichModelsWithDetailsAsync(IEnumerable<Model> models)
        {
            // Get unique, non-empty titles
            var uniqueTitles = models
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.title))
                .Select(m => m.title)
                .Distinct()
                .ToList();

            await QueryAndStoreModelDetailsAsync(uniqueTitles).ConfigureAwait(false);

            // Assign enrichment to each model
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

        /// <summary>
        /// Enriches a single model with data from the API and assigns the enrichment to the model.
        /// </summary>
        /// <param name="model">A Model object to enrich.</param>
        public async Task EnrichModel(Model model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.title))
                return;

            // Query and store details for this model if not already present
            if (!_modelDetails.ContainsKey(model.title))
            {
                await QueryAndStoreModelDetailsAsync([model.title]);
            }

            if (_modelDetails.TryGetValue(model.title, out var enriched))
            {
                model.enrichedData = enriched;
            }
        }

    }
}