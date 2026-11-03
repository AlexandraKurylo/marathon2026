using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace RestaurantServer;

public sealed class FakeSqs(ServerSettings settings, ILogger<FakeSqs> logger)
{
    public Task HandleAsync(HttpContext context) =>
        AwsJson.HandleAsync(context, "application/x-amz-json-1.0", Dispatch);

    private JsonObject Dispatch(string operation, JsonObject input)
    {
        switch (operation)
        {
            case "GetQueueUrl":
                var name = AwsJson.RequiredString(input, "QueueName");
                return new JsonObject { ["QueueUrl"] = $"{settings.InternalUrl}/queues/{name}" };

            case "SendMessage":
                var body = AwsJson.RequiredString(input, "MessageBody");
                logger.LogInformation("Message accepted by local SQS emulator ({Length} chars). Reports are not processed locally.", body.Length);
                return new JsonObject
                {
                    ["MessageId"] = Guid.NewGuid().ToString(),
                    ["MD5OfMessageBody"] = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(body))),
                };

            default:
                throw new AwsServiceException("InvalidAction", $"Operation {operation} is not supported by the local SQS emulator.");
        }
    }
}
