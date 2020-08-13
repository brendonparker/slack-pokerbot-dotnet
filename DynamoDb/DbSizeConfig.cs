
using Amazon.DynamoDBv2.DataModel;

namespace slack_pokerbot_dotnet
{
    [DynamoDBTable("pokerbot")]
    public class DbSizeConfig
    {
        [DynamoDBHashKey("channel")]
        public string TeamAndChannel { get; set; }
        [DynamoDBRangeKey("key")]
        public string Key
        {
            get => $"Config";
            set { }
        }

        public SizeConfig Attributes { get; set; }
    }
}
