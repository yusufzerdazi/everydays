using System.Collections.Generic;
using Newtonsoft.Json;

namespace Everydays
{
    public class InstagramPost
    {
        [JsonProperty("tags")]
        public List<string> Tags { get; set; }
        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }
        [JsonProperty("title")]
        public string Title { get; set; }
        [JsonProperty("permalink")]
        public string Permalink { get; set; }
    }
}
