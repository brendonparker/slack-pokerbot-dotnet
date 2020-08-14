using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace slack_pokerbot_dotnet
{
    public partial class WebApiFunction
    {
        private readonly AmazonSimpleNotificationServiceClient snsClient;

        private string TOPIC_ARN => Environment.GetEnvironmentVariable("TOPIC_ARN");

        public WebApiFunction()
        {
            snsClient = new AmazonSimpleNotificationServiceClient();
        }

        /// <summary>
        /// A Lambda function to respond to HTTP Post methods from API Gateway
        /// </summary>
        /// <param name="request"></param>
        /// <returns>The API Gateway response.</returns>
        public async Task<APIGatewayProxyResponse> Post(APIGatewayProxyRequest request, ILambdaContext context)
        {
            Console.WriteLine("Post Request\n");

            var slackEvent = SlackEvent.FromFormEncodedData(request.Body);

            Console.WriteLine("Slack Event: " + JsonConvert.SerializeObject(slackEvent));

            // Offload the prossing to the SNS lambda
            // Sometimes dynamodb startup is too slow, if we
            // wait too long, Slack gives up on the command
            await snsClient.PublishAsync(new PublishRequest
            {
                TopicArn = TOPIC_ARN,
                Message = JsonConvert.SerializeObject(slackEvent),
            });

            // Return an OK status code ASAP
            // This keeps Slack happy, since it will only wait a few
            // seconds for an acknowledgement
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK
            };
        }
    }
}
