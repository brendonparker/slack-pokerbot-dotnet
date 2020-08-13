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

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace slack_pokerbot_dotnet
{
    public class Functions
    {
        private readonly AmazonDynamoDBClient client;
        private readonly DynamoDBContext context;

        public Functions()
        {
            client = new AmazonDynamoDBClient();
            context = new DynamoDBContext(client);
        }

        /// <summary>
        /// A Lambda function to respond to HTTP Post methods from API Gateway
        /// </summary>
        /// <param name="request"></param>
        /// <returns>The API Gateway response.</returns>
        public APIGatewayProxyResponse Post(APIGatewayProxyRequest request, ILambdaContext context)
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
            var subcommand = commandArguments[0];

            context.Logger.LogLine($"subCommand: {subcommand}");

            switch (subcommand)
            {
                case "setup":
                    break;
                case "start":
                    break;
                case "deal":
                    break;
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
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(new { text }),
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
