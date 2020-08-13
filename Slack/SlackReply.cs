using System.Collections.Generic;

namespace slack_pokerbot_dotnet
{
    public class SlackReply
    {
        public string text { get; set; }
        public string response_type { get; set; }
        public IEnumerable<SlackAttachment> attachments { get; set; }
    }
}
