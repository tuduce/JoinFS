using Newtonsoft.Json;
using System.Collections.Generic;

namespace JoinFS.DataModel
{
    public class EnrichedModelResponse
    {
        [JsonProperty("found_count")]
        public int FoundCount { get; set; }

        [JsonProperty("buffered_count")]
        public int BufferedCount { get; set; }

        [JsonProperty("data")]
        public List<EnrichedAircraftData> Data { get; set; }
    }
}
