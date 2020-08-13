
using Amazon.DynamoDBv2.DataModel;

namespace slack_pokerbot_dotnet
{
    [DynamoDBTable("pokerbot")]
    public class DbPokerSession
    {
        [DynamoDBHashKey("channel")]
        public string TeamAndChannel { get; set; }
        [DynamoDBRangeKey("key")]
        public string Key
        {
            get => $"Session|{Attributes.Id}";
            set { }
        }

        public PokerSession Attributes { get; set; }
    }
}
