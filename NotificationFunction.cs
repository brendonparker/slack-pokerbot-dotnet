using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.SNSEvents;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace slack_pokerbot_dotnet
{
    public partial class NotificationFunction
    {
        private readonly AmazonDynamoDBClient client;
        private readonly DynamoDBContext dbContext;
        private readonly SizeRepo sizeRepo;
        private static HttpClient httpClient = new HttpClient();

        public NotificationFunction()
        {
            client = new AmazonDynamoDBClient();
            dbContext = new DynamoDBContext(client);
            sizeRepo = new SizeRepo();
        }

        public async Task FunctionHandler(SNSEvent evnt, ILambdaContext context)
        {
            foreach (var record in evnt.Records)
            {
                var msg = record.Sns.Message;
                var slackEvent = JsonConvert.DeserializeObject<SlackEvent>(msg);

                if (!IsValidSlackToken(slackEvent.token))
                {
                    Console.WriteLine($"Invalid Slack Token: {slackEvent.token}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(slackEvent.text))
                {
                    await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                    {
                        text = "Type */poker help* for pokerbot commands."
                    });
                    continue;
                }

                var commandArguments = slackEvent.text.Split(' ');

                // The dynamodb can take several seconds to initiate its setup/sslhandshake on the first run
                // So...
                // Go ahead and return an OK response, and handle the processing of the command in a separate task
                await HandleCommandAsync(slackEvent, commandArguments);
            }
        }

        private bool IsValidSlackToken(string token)
        {
            var configuredSlackToken = Environment.GetEnvironmentVariable("SLACK_TOKEN");
            var validTokens = configuredSlackToken.Split(new[] { ' ', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            return validTokens.Contains(token);
        }

        private async Task HandleCommandAsync(
            SlackEvent slackEvent,
            string[] commandArguments)
        {
            var command = commandArguments[0];
            Console.WriteLine($"command: {command}");

            switch (command)
            {
                case "setup":
                    {
                        if (commandArguments.Length < 2)
                        {
                            await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                            {
                                text = "You must enter a size format `/poker setup [f, s, t, m]`."
                            });
                            return;
                        }

                        var size = commandArguments[1];

                        if (!sizeRepo.IsValidSize(size))
                        {
                            await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                            {
                                text = $"Your choices are {sizeRepo.ListOfValidSizes()} in format /poker setup <choice>."
                            });
                            return;
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

                        await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                        {
                            text = "Size has been set for channel."
                        });

                        return;
                    }
                case "start":
                    await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                    {
                        text = "No need to start, just *deal*"
                    });
                    return;
                case "deal":
                    {
                        if (commandArguments.Length < 2)
                        {
                            await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                            {
                                text = "You did not enter a JIRA ticket number."
                            });
                            return;
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
                        Console.WriteLine($"Saved Session in {sw.ElapsedMilliseconds}ms");


                        sw.Restart();
                        var config = await dbContext.LoadAsync<DbSizeConfig>(slackEvent.TeamAndChannel, "Config");
                        sw.Stop();
                        Console.WriteLine($"Loaded config in {sw.ElapsedMilliseconds}ms");

                        await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
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

                        return;
                    }
                case "vote":
                    {
                        var config = await dbContext.LoadAsync<DbSizeConfig>(slackEvent.TeamAndChannel, "Config");
                        var session = await GetCurrentSession(slackEvent);

                        if (config == null || session == null)
                        {
                            await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                            {
                                text = "The poker planning game hasn't started yet."
                            });
                            return;
                        }

                        if (commandArguments.Length < 2)
                        {
                            await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                            {
                                text = "Your vote was not counted. You didn't enter a size."
                            });
                            return;
                        }

                        var voteVal = commandArguments[1];

                        var sizeValues = sizeRepo.GetSize(config.Attributes.Size);
                        if (!sizeValues.ContainsKey(voteVal))
                        {
                            var validSizes = string.Join(", ", sizeValues.Keys);
                            await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                            {
                                text = "Your vote was not counted. Please enter a valid poker planning size: " + validSizes
                            });
                            return;
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

                        await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                        {
                            text = $"{slackEvent.user_name} voted!",
                            response_type = "in_channel"
                        });

                        return;
                    }
                case "tally":
                    {
                        var session = await GetCurrentSession(slackEvent);

                        if (session == null)
                        {
                            await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                            {
                                text = "The poker planning game hasn't started yet."
                            });
                            return;
                        }

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

                        await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                        {
                            text = msg,
                            response_type = "in_channel"
                        });

                        return;
                    }
                case "reveal":
                    {
                        var session = await GetCurrentSession(slackEvent);
                        if (session == null)
                        {
                            await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                            {
                                text = "The poker planning game hasn't started yet."
                            });
                            return;
                        }

                        if (!session.Attributes.Votes.Any())
                        {
                            await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                            {
                                text = "No one voted :sad:"
                            });
                            return;
                        }

                        var mostRecentVotes = session.Attributes.Votes
                            .GroupBy(x => x.UserId)
                            .Select(x => x.OrderByDescending(x => x.Timestamp).First())
                            .ToArray();

                        var config = await dbContext.LoadAsync<DbSizeConfig>(slackEvent.TeamAndChannel, "Config");
                        var validValues = sizeRepo.GetSize(config.Attributes.Size);

                        if (mostRecentVotes.Select(x => x.Vote).Distinct().Count() == 1)
                        {
                            var voteVal = mostRecentVotes.First().Vote;
                            await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
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
                            return;
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

                            await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                            {
                                text = "*No winner yet.* Discuss and continue voting.",
                                attachments = attachments,
                                response_type = "in_channel"
                            });
                            return;
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

                        await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                        {
                            text = msg
                        });

                        return;
                    }
                case "help":
                    await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
                    {
                        text = @"Pokerbot helps you play Agile/Scrum poker planning.

Use the following commands:
 /poker setup
 /poker deal
 /poker vote
 /poker tally
 /poker reveal
 /poker history"
                    });
                    return;
            }
            await SendDelayedMessageAsync(slackEvent.response_url, new SlackReply
            {
                text = "Invalid command. Type */poker help* for pokerbot commands."
            });
            return;
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
    }
}
