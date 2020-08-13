
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Web;

namespace slack_pokerbot_dotnet
{
    public class SlackEvent
    {
        public static SlackEvent FromFormEncodedData(string formEncodedData)
        {
            var decodedBody = HttpUtility.UrlDecode(formEncodedData);
            var qs = HttpUtility.ParseQueryString(decodedBody);
            var keyValuePairs = new Dictionary<string, string>();

            foreach (var key in qs.AllKeys)
            {
                keyValuePairs[key] = qs[key];
            }

            // Hacky conversion 
            return JsonConvert.DeserializeObject<SlackEvent>(JsonConvert.SerializeObject(keyValuePairs));
        }

        public string token { get; set; }
        public string team_id { get; set; }
        public string team_domain { get; set; }
        public string enterprise_id { get; set; }
        public string enterprise_name { get; set; }
        public string channel_id { get; set; }
        public string channel_name { get; set; }
        public string user_id { get; set; }
        public string user_name { get; set; }
        public string command { get; set; }
        public string text { get; set; }
        public string response_url { get; set; }
        public string trigger_id { get; set; }
        public string api_app_id { get; set; }
    }
}
