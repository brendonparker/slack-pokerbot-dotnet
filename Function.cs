using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Newtonsoft.Json;
using Amazon.DynamoDBv2.DocumentModel;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace slack_pokerbot_dotnet
{
    public class Functions
    {
        private readonly AmazonDynamoDBClient client;
        private readonly DynamoDBContext dbContext;
        private readonly SizeRepo sizeRepo;

        public Functions()
        {
            client = new AmazonDynamoDBClient();
            dbContext = new DynamoDBContext(client);
            sizeRepo = new SizeRepo();
        }

        /// <summary>
        /// A Lambda function to respond to HTTP Post methods from API Gateway
        /// </summary>
        /// <param name="request"></param>
        /// <returns>The API Gateway response.</returns>
        public async Task<APIGatewayProxyResponse> Post(APIGatewayProxyRequest request, ILambdaContext context)
        {
            context.Logger.LogLine("Post Request\n");

            var slackEvent = SlackEvent.FromFormEncodedData(request.Body);

            context.Logger.LogLine("Slack Event: " + JsonConvert.SerializeObject(slackEvent));

            if (!IsValidSlackToken(slackEvent.token))
                throw new Exception("Invalid Slack Token");

            if (string.IsNullOrWhiteSpace(slackEvent.text))
            {
                return CreateEphemeralResponse("Type */poker help* for pokerbot commands.");
            }

            var commandArguments = slackEvent.text.Split(' ');
            var command = commandArguments[0];

            context.Logger.LogLine($"command: {command}");

            switch (command)
            {
                case "setup":
                    {
                        if (commandArguments.Length < 2)
                        {
                            return CreateEphemeralResponse("You must enter a size format `/poker setup [f, s, t, m]`.");
                        }

                        var size = commandArguments[1];

                        if (!sizeRepo.IsValidSize(size))
                        {
                            return CreateEphemeralResponse($"Your choices are {sizeRepo.ListOfValidSizes()} in format /poker setup <choice>.");
                        }

                        await dbContext.SaveAsync(new DbSizeConfig
                        {
                            TeamAndChannel = slackEvent.TeamAndChannel,
                            Key = $"Config",
                            Attributes = new SizeConfig
                            {
                                Size = size,
                                LastUpdated = DateTime.UtcNow
                            }
                        });

                        return CreateEphemeralResponse("Size has been set for channel.");
                    }
                case "deal":
                    if (commandArguments.Length < 2)
                    {
                        return CreateEphemeralResponse("You did not enter a JIRA ticket number.");
                    }

                    var ticketNumber = commandArguments[1];

                    await dbContext.SaveAsync(new DbPokerSession
                    {
                        TeamAndChannel = slackEvent.TeamAndChannel,
                        Attributes = new PokerSession
                        {
                            JiraTicket = ticketNumber
                        }
                    });

                    //var queryConfig = new DynamoDBOperationConfig
                    //{
                    //    BackwardQuery = true
                    //};
                    //var set = await dbContext
                    //    .QueryAsync<DbPokerSession>(slackEvent.TeamAndChannel, QueryOperator.BeginsWith, new[] { "Session|" }, queryConfig)
                    //    .GetNextSetAsync();

                    var config = await dbContext.LoadAsync<DbSizeConfig>(slackEvent.TeamAndChannel, "Config");

                    //var session = set.First();
                    //session.Attributes.JiraTicket = ticketNumber;
                    //await dbContext.SaveAsync(session);

                    return CreateMessage($"*The planning poker game has started* for {ticketNumber}", new[]
                    {
                        new SlackAttachment
                        {
                            Text = "Vote by typing */poker vote <size>*.",
                            ImageUrl = sizeRepo.GetCompositeImage(config.Attributes.Size)
                        }
                    });
                case "vote":
                    break;
                case "tally":
                    break;
                case "reveal":
                    break;
                case "end":
                    break;
            }
            return CreateEphemeralResponse("Invalid command. Type */poker help* for pokerbot commands.");
        }

        public APIGatewayProxyResponse CreateEphemeralResponse(string text)
        {
            Console.WriteLine($"CreateEphemeralResponse: {text}");
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(new { text }),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }

        public APIGatewayProxyResponse CreateMessage(string text, IEnumerable<SlackAttachment> attachments = null)
        {
            Console.WriteLine($"CreateMessage: {text}");
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(new { text, attachments }),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }


        private bool IsValidSlackToken(string token)
        {
            var configuredSlackToken = Environment.GetEnvironmentVariable("SLACK_TOKEN");
            var validTokens = configuredSlackToken.Split(new[] { ' ', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            return validTokens.Contains(token);
        }

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

        public class PokerSession
        {
            public long Id { get; set; } = DateTime.UtcNow.Ticks;
            public DateTime StartedOn { get; set; } = DateTime.UtcNow;
            public string JiraTicket { get; set; }
        }

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

        public class SizeConfig
        {
            public string Size { get; set; }
            public DateTime LastUpdated { get; set; }
        }
    }

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
