using Newtonsoft.Json;
using System.Collections.Generic;

namespace JoinFS.DataModel
{
    public class ModelCheckRequest
    {
        [JsonProperty("models")]
        public List<string> Models { get; set; }
    }
}
