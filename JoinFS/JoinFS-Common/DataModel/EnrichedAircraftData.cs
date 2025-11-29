using Newtonsoft.Json;

namespace JoinFS.DataModel
{
    public class EnrichedAircraftData
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("typerole")]
        public string TypeRole { get; set; }

        [JsonProperty("wingposition")]
        public string WingPosition { get; set; }

        [JsonProperty("engines")]
        public int Engines { get; set; }

        [JsonProperty("enginetype")]
        public string EngineType { get; set; }

        [JsonProperty("wake")]
        public string Wake { get; set; }

        [JsonProperty("military")]
        public bool Military { get; set; }
    }
}
