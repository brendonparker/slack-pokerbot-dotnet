
using Newtonsoft.Json;

namespace slack_pokerbot_dotnet
{

    public class SlackAttachment
    {
        [JsonProperty("text")]
        public string Text { get; set; }
        [JsonProperty("color")]
        public string Color { get; set; }
        [JsonProperty("image_url")]
        public string ImageUrl { get; set; }
        [JsonProperty("thumb_url")]
        public string ThumbUrl { get; set; }
    }
}
