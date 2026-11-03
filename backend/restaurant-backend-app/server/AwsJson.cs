using System.Text.Json.Nodes;

namespace RestaurantServer;

public sealed class AwsServiceException(string type, string message, int statusCode = 400) : Exception(message)
{
    public string Type { get; } = type;
    public int StatusCode { get; } = statusCode;
}

public static class AwsJson
{
    public static async Task HandleAsync(HttpContext context, string contentType, Func<string, JsonObject, JsonObject> dispatch)
    {
        var target = context.Request.Headers["X-Amz-Target"].ToString();
        var operation = target[(target.LastIndexOf('.') + 1)..];

        using var reader = new StreamReader(context.Request.Body);
        var raw = await reader.ReadToEndAsync();
        var input = (string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw)) as JsonObject ?? new JsonObject();

        context.Response.ContentType = contentType;
        context.Response.Headers["x-amzn-RequestId"] = Guid.NewGuid().ToString();

        try
        {
            var output = dispatch(operation, input);
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync(output.ToJsonString());
        }
        catch (AwsServiceException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            context.Response.Headers["x-amzn-ErrorType"] = ex.Type;
            var error = new JsonObject { ["__type"] = ex.Type, ["message"] = ex.Message };
            await context.Response.WriteAsync(error.ToJsonString());
        }
    }

    public static string RequiredString(JsonObject input, string name) =>
        input[name]?.GetValue<string>() is { Length: > 0 } value
            ? value
            : throw new AwsServiceException("InvalidParameterException", $"Missing required parameter {name}");

    public static string? OptionalString(JsonObject input, string name) =>
        input[name]?.GetValue<string>();
}
