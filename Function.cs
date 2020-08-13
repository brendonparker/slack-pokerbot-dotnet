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

                        return CreateMessage(new SlackReply
                        {
                            text = $"*The planning poker game has started* for {ticketNumber}",
                            attachments = new[]
                            {
                                new SlackAttachment
                                {
                                    Text = "Vote by typing */poker vote <size>*.",
                                    ImageUrl = sizeRepo.GetCompositeImage(config.Attributes.Size)
                                }
                            },
                            response_type = "in_channel"
                        });
                    }
                case "vote":
                    {
                        var config = await dbContext.LoadAsync<DbSizeConfig>(slackEvent.TeamAndChannel, "Config");
                        var session = await GetCurrentSession(slackEvent);

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
                            text = $"{slackEvent.user_name} voted!",
                            response_type = "in_channel"
                        });

                        return CreateEphemeralResponse($"You voted *{voteVal}*");
                    }
                case "tally":
                    {
                        var session = await GetCurrentSession(slackEvent);

                        if (session == null)
                            return CreateEphemeralResponse("The poker planning game hasn't started yet.");

                        var userNames = session.Attributes
                            .Votes
                            .GroupBy(x => x.UserId)
                            .Select(x => x.First().UserName)
                            .ToList();

                        var msg = $"{string.Join(", ", userNames)} have voted";

                        if (userNames.Count == 0)
                            msg = "No one has voted yet";
                        if (userNames.Count == 1)
                            msg = $"{userNames[0]} has voted";

                        return CreateMessage(new SlackReply { text = msg, response_type = "in_channel" });
                    }
                case "reveal":
                    {
                        var session = await GetCurrentSession(slackEvent);
                        if (session == null)
                            return CreateEphemeralResponse("The poker planning game hasn't started yet.");

                        if (!session.Attributes.Votes.Any())
                            return CreateEphemeralResponse("No one voted :sad:");

                        var mostRecentVotes = session.Attributes.Votes
                            .GroupBy(x => x.UserId)
                            .Select(x => x.OrderByDescending(x => x.Timestamp).First())
                            .ToArray();

                        var config = await dbContext.LoadAsync<DbSizeConfig>(slackEvent.TeamAndChannel, "Config");
                        var validValues = sizeRepo.GetSize(config.Attributes.Size);

                        if (mostRecentVotes.Select(x => x.Vote).Distinct().Count() == 1)
                        {
                            var voteVal = mostRecentVotes.First().Vote;
                            return CreateMessage(new SlackReply
                            {
                                text = "*Congratulations!*",
                                attachments = new[]
                                {
                                    new SlackAttachment
                                    {
                                        Text = "Everyone selected the same number",
                                        Color = "good",
                                        ImageUrl = validValues[voteVal]
                                    }
                                },
                                response_type = "in_channel"
                            });
                        }
                        else
                        {
                            var attachments = mostRecentVotes.GroupBy(x => x.Vote)
                                .Select(grp =>
                                {
                                    var userNames = string.Join(", ", grp.Select(x => x.UserName));
                                    return new SlackAttachment
                                    {
                                        Text = userNames,
                                        Color = "warning",
                                        ImageUrl = validValues[grp.Key]
                                    };
                                });

                            return CreateMessage(new SlackReply
                            {
                                text = "*No winner yet.* Discuss and continue voting.",
                                attachments = attachments,
                                response_type = "in_channel"
                            });
                        }
                    }
                case "report":
                case "history":
                    {
                        var queryConfig = new DynamoDBOperationConfig
                        {
                            // Trying to get the session "at the top of the stack"
                            BackwardQuery = true
                        };
                        var results = await dbContext
                            .QueryAsync<DbPokerSession>(slackEvent.TeamAndChannel, QueryOperator.BeginsWith, new[] { "Session|" }, queryConfig)
                            .GetNextSetAsync();

                        var previousSessions = results.Select(x => x.Attributes).Take(10).ToList();

                        var msg = PlainTextTable.ToTable(Report(previousSessions));

                        return CreateEphemeralResponse(msg);
                    }
                case "help":
                    return CreateEphemeralResponse(@"Pokerbot helps you play Agile/Scrum poker planning.

Use the following commands:
 /poker setup
 /poker deal
 /poker vote
 /poker tally
 /poker reveal
 /poker history");
            }
            return CreateEphemeralResponse("Invalid command. Type */poker help* for pokerbot commands.");
        }

        private async Task<DbPokerSession> GetCurrentSession(SlackEvent slackEvent)
        {
            var queryConfig = new DynamoDBOperationConfig
            {
                // Trying to get the session "at the top of the stack"
                BackwardQuery = true
            };
            var results = await dbContext
                .QueryAsync<DbPokerSession>(slackEvent.TeamAndChannel, QueryOperator.BeginsWith, new[] { "Session|" }, queryConfig)
                .GetNextSetAsync();

            return results.FirstOrDefault();
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

        public APIGatewayProxyResponse CreateMessage(SlackReply msg)
        {
            Console.WriteLine($"CreateMessage: {msg.text}");
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(msg),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }

        public IEnumerable<VoteReportRow> Report(List<PokerSession> sessions)
        {
            foreach (var session in sessions)
            {
                var votes = session.Votes
                    .GroupBy(x => x.UserId)
                    .Select(grp => grp.OrderByDescending(x => x.Timestamp).First());

                foreach (var vote in votes.GroupBy(x => x.Vote))
                {
                    yield return new VoteReportRow
                    {
                        Story = session.JiraTicket,
                        Date = session.StartedOn.ToShortDateString(),
                        Vote = vote.Key,
                        Count = vote.Count(),
                        People = string.Join(", ", vote.Select(x => x.UserName))
                    };
                }
            }
        }

        private bool IsValidSlackToken(string token)
        {
            var configuredSlackToken = Environment.GetEnvironmentVariable("SLACK_TOKEN");
            var validTokens = configuredSlackToken.Split(new[] { ' ', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            return validTokens.Contains(token);
        }
    }
}
