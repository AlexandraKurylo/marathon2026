using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using System.Text;
using LambdaFunction = APIHandler.Function;

namespace RestaurantServer;

public sealed class LambdaBridge(ServerSettings settings, ApiAuthorizer authorizer, ILogger<LambdaBridge> logger)
{
    private const string StagePrefix = "/develop";
    private readonly Lazy<LambdaFunction> _function = new(() => new LambdaFunction());
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<APIGatewayProxyResponse> InvokeAsync(APIGatewayProxyRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            var context = new LocalLambdaContext(logger);
            return await _function.Value.FunctionHandler(request, context);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task HandleAsync(HttpContext http)
    {
        var path = http.Request.Path.Value ?? "/";
        if (path.StartsWith(StagePrefix + "/", StringComparison.OrdinalIgnoreCase) || path.Equals(StagePrefix, StringComparison.OrdinalIgnoreCase))
            path = path[StagePrefix.Length..];
        if (path.Length == 0)
            path = "/";

        var method = http.Request.Method.ToUpperInvariant();
        Dictionary<string, string>? claims = null;

        if (authorizer.RequiresAuthorization(method, path))
        {
            claims = authorizer.GetClaims(http.Request.Headers.Authorization.ToString());
            if (claims is null)
            {
                Cors.Apply(http.Response);
                http.Response.StatusCode = 401;
                http.Response.ContentType = "application/json";
                await http.Response.WriteAsync("{\"message\":\"Unauthorized\"}");
                return;
            }
        }

        using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        var request = new APIGatewayProxyRequest
        {
            HttpMethod = method,
            Path = path,
            Resource = path,
            Body = body.Length == 0 ? null : body,
            IsBase64Encoded = false,
            Headers = http.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            MultiValueHeaders = http.Request.Headers.ToDictionary(h => h.Key, h => (IList<string>)h.Value.Select(v => v ?? string.Empty).ToList(), StringComparer.OrdinalIgnoreCase),
            QueryStringParameters = http.Request.Query.Count == 0
                ? null
                : http.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString()),
            MultiValueQueryStringParameters = http.Request.Query.Count == 0
                ? null
                : http.Request.Query.ToDictionary(q => q.Key, q => (IList<string>)q.Value.Select(v => v ?? string.Empty).ToList()),
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
            {
                HttpMethod = method,
                Path = StagePrefix + path,
                ResourcePath = path,
                Stage = StagePrefix.TrimStart('/'),
                RequestId = Guid.NewGuid().ToString(),
                Identity = new APIGatewayProxyRequest.RequestIdentity
                {
                    SourceIp = http.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = http.Request.Headers.UserAgent.ToString(),
                },
                Authorizer = claims is null ? null : new APIGatewayCustomAuthorizerContext { Claims = claims },
            },
        };

        APIGatewayProxyResponse response;
        try
        {
            response = await InvokeAsync(request);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error for {Method} {Path}", method, path);
            Cors.Apply(http.Response);
            http.Response.StatusCode = 502;
            http.Response.ContentType = "application/json";
            await http.Response.WriteAsync("{\"message\":\"Internal server error\"}");
            return;
        }

        await WriteResponseAsync(http, response);
    }

    private async Task WriteResponseAsync(HttpContext http, APIGatewayProxyResponse response)
    {
        http.Response.StatusCode = response.StatusCode == 0 ? 200 : response.StatusCode;

        if (response.Headers is not null)
        {
            foreach (var (key, value) in response.Headers)
                http.Response.Headers[key] = value;
        }

        if (response.MultiValueHeaders is not null)
        {
            foreach (var (key, values) in response.MultiValueHeaders)
                http.Response.Headers[key] = values.ToArray();
        }

        Cors.Apply(http.Response);

        if (string.IsNullOrEmpty(response.Body))
            return;

        if (response.IsBase64Encoded)
        {
            var bytes = Convert.FromBase64String(response.Body);
            http.Response.ContentLength = bytes.Length;
            await http.Response.Body.WriteAsync(bytes);
            return;
        }

        var text = settings.RewriteInternalUrls(response.Body);
        var data = Encoding.UTF8.GetBytes(text);
        http.Response.ContentLength = data.Length;
        await http.Response.Body.WriteAsync(data);
    }

    private sealed class LocalLambdaContext(ILogger logger) : ILambdaContext
    {
        public string AwsRequestId { get; } = Guid.NewGuid().ToString();
        public IClientContext ClientContext => null!;
        public string FunctionName => "api-handler";
        public string FunctionVersion => "$LATEST";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:local:000000000000:function:api-handler";
        public ILambdaLogger Logger { get; } = new LocalLambdaLogger(logger);
        public string LogGroupName => "/local/api-handler";
        public string LogStreamName => "local";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);
    }

    private sealed class LocalLambdaLogger(ILogger logger) : ILambdaLogger
    {
        public void Log(string message) => logger.LogInformation("{Message}", message);
        public void LogLine(string message) => logger.LogInformation("{Message}", message);
    }
}

public static class Cors
{
    public static void Apply(HttpResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-Guest-Session-Token, X-Requested-With, Accept";
        response.Headers["Access-Control-Expose-Headers"] = "Content-Disposition";
    }
}
