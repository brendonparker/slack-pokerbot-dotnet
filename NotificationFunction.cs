using Amazon.Lambda.Core;
using Amazon.Lambda.SNSEvents;
using System.Threading.Tasks;

namespace slack_pokerbot_dotnet
{
    public partial class NotificationFunction
    {
        public Task FunctionHandler(SNSEvent evnt, ILambdaContext context)
        {
            return Task.CompletedTask;
        }
    }
}
