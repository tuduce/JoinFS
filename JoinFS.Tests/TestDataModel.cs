using Newtonsoft.Json;

namespace JoinFS.DataModel;

public class EnrichedAircraftData
{
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonProperty("typerole")]
    public string TypeRole { get; set; } = string.Empty;

    [JsonProperty("wingposition")]
    public string WingPosition { get; set; } = string.Empty;

    [JsonProperty("engines")]
    public int Engines { get; set; }

    [JsonProperty("enginetype")]
    public string EngineType { get; set; } = string.Empty;

    [JsonProperty("wake")]
    public string Wake { get; set; } = string.Empty;

    [JsonProperty("military")]
    public bool Military { get; set; }
}

public class ModelCheckRequest
{
    [JsonProperty("models")]
    public List<string> Models { get; set; } = new List<string>();
}

public class EnrichedModelResponse
{
    [JsonProperty("found_count")]
    public int FoundCount { get; set; }

    [JsonProperty("buffered_count")]
    public int BufferedCount { get; set; }

    [JsonProperty("data")]
    public List<EnrichedAircraftData> Data { get; set; } = new List<EnrichedAircraftData>();
}
