using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;

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
        public APIGatewayProxyResponse Get(APIGatewayProxyRequest request, ILambdaContext context)
        {
            context.Logger.LogLine("Post Request\n");

            var slackEvent = SlackEvent.FromFormEncodedData(request.Body);

            if (!IsValidSlackToken(slackEvent.token))
                throw new Exception("Invalid Slack Token");

            var response = new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = "Hello AWS Serverless",
                Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
            };

            return response;
        }

        private bool IsValidSlackToken(string token)
        {
            var configuredSlackToken = Environment.GetEnvironmentVariable("SlackToken");
            var validTokens = configuredSlackToken.Split(new[] { ' ', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            return validTokens.Contains(token);
        }
    }
}
