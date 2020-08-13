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
using Amazon.DynamoDBv2.Model;
using System.Net.Http;
using System.Text;
using System.Globalization;
using System.Diagnostics;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace slack_pokerbot_dotnet
{
    public partial class Functions
    {
        private readonly AmazonDynamoDBClient client;
        private readonly DynamoDBContext dbContext;
        private readonly SizeRepo sizeRepo;
        private static HttpClient httpClient = new HttpClient();

        public Functions()
        {
            client = new AmazonDynamoDBClient();
            dbContext = new DynamoDBContext(client);
            sizeRepo = new SizeRepo();

            // Hack: Warm up dbContext
            // Intentionally fire and forget
            dbContext.LoadAsync<DbSizeConfig>("", "");
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
                case "start":
                    return CreateEphemeralResponse("No need to start, just *deal*");
                case "deal":
                    {
                        if (commandArguments.Length < 2)
                        {
                            return CreateEphemeralResponse("You did not enter a JIRA ticket number.");
                        }

                        var ticketNumber = commandArguments[1];

                        var sw = new Stopwatch();
                        sw.Start();
                        await dbContext.SaveAsync(new DbPokerSession
                        {
                            TeamAndChannel = slackEvent.TeamAndChannel,
                            Attributes = new PokerSession
                            {
                                JiraTicket = ticketNumber
                            }
                        });
                        sw.Stop();
                        context.Logger.LogLine($"Saved Session in {sw.ElapsedMilliseconds}ms");


                        sw.Restart();
                        var config = await dbContext.LoadAsync<DbSizeConfig>(slackEvent.TeamAndChannel, "Config");
                        sw.Stop();
                        context.Logger.LogLine($"Loaded config in {sw.ElapsedMilliseconds}ms");

                        return CreateMessage($"*The planning poker game has started* for {ticketNumber}", new[]
                        {
                            new SlackAttachment
                            {
                                Text = "Vote by typing */poker vote <size>*.",
                                ImageUrl = sizeRepo.GetCompositeImage(config.Attributes.Size)
                            }
                        });
                    }
                case "vote":
                    {
                        var config = await dbContext.LoadAsync<DbSizeConfig>(slackEvent.TeamAndChannel, "Config");

                        var queryConfig = new DynamoDBOperationConfig
                        {
                            BackwardQuery = true
                        };
                        var results = await dbContext
                            .QueryAsync<DbPokerSession>(slackEvent.TeamAndChannel, QueryOperator.BeginsWith, new[] { "Session|" }, queryConfig)
                            .GetNextSetAsync();

                        var session = results.FirstOrDefault();

                        if (config == null || session == null)
                            return CreateEphemeralResponse("The poker planning game hasn't started yet.");

                        if (commandArguments.Length < 2)
                            return CreateEphemeralResponse("Your vote was not counted. You didn't enter a size.");

                        var voteVal = commandArguments[1];

                        var sizeValues = sizeRepo.GetSize(config.Attributes.Size);
                        if (!sizeValues.ContainsKey(voteVal))
                        {
                            var validSizes = string.Join(", ", sizeValues.Keys);
                            return CreateEphemeralResponse("Your vote was not counted. Please enter a valid poker planning size: " + validSizes);
                        }

                        var pokerVote = new PokerSessionVote
                        {
                            UserId = slackEvent.user_id,
                            UserName = slackEvent.user_name,
                            Timestamp = DateTime.UtcNow.Ticks,
                            Vote = voteVal
                        };

                        // Hacky hack hack to get AttributeValue for PokerSessionVote
                        var doc = Document.FromJson(JsonConvert.SerializeObject(new { temp = new[] { pokerVote } }));
                        var voteAttributeValue = doc.ToAttributeMap()["temp"];

                        await client.UpdateItemAsync(new UpdateItemRequest
                        {
                            TableName = "pokerbot",
                            Key = new Dictionary<string, AttributeValue>
                            {
                                { "channel", new AttributeValue(session.TeamAndChannel) },
                                { "key", new AttributeValue(session.Key) },
                            },
                            UpdateExpression = "SET #ATTRIBUTES.#VOTES = list_append(if_not_exists(#ATTRIBUTES.#VOTES, :empty_list), :VOTE)",
                            ExpressionAttributeNames = new Dictionary<string, string>
                            {
                                { "#ATTRIBUTES", "Attributes" },
                                { "#VOTES", "Votes" }
                            },
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                            {
                                {":VOTE", voteAttributeValue },
                                {":empty_list", new AttributeValue { L = new List<AttributeValue>(), IsLSet = true } }
                            }
                        });

                        // Intentionally fire-and-forget
                        SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                        {
                            text = $"{slackEvent.user_name} voted!"
                        });

                        return CreateEphemeralResponse($"You voted *{voteVal}*");
                    }
                case "tally":
                    break;
                case "reveal":
                    break;
                case "end":
                    break;
            }
            return CreateEphemeralResponse("Invalid command. Type */poker help* for pokerbot commands.");
        }

        public async Task SendDelayedMessageAsync(string requestUri, SlackReply slackMessage)
        {
            var strJson = JsonConvert.SerializeObject(slackMessage);
            Console.WriteLine($"SendDelayedMessageAsync: {strJson}");
            using (var content = new StringContent(strJson, Encoding.UTF8, "application/json"))
            {
                try
                {
                    var res = await httpClient.PostAsync(requestUri, content);
                    Console.WriteLine($"SendDelayedMessage - Status Code: {res.StatusCode}");
                    if (!res.IsSuccessStatusCode)
                    {
                        throw new Exception(await res.Content.ReadAsStringAsync());
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SendDelayedMessage Failed! {ex.Message}");
                }
            }
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
    }
}
