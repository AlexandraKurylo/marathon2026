using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RestaurantServer;

public sealed class ApiAuthorizer
{
    private readonly JwtService _jwt;
    private readonly List<(string Method, Regex Pattern)> _securedRoutes = [];

    public ApiAuthorizer(JwtService jwt, ServerSettings settings, ILogger<ApiAuthorizer> logger)
    {
        _jwt = jwt;

        if (!File.Exists(settings.ApiResourcesFile))
        {
            logger.LogWarning("{File} not found: every route is treated as public.", settings.ApiResourcesFile);
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(settings.ApiResourcesFile));
        foreach (var resource in document.RootElement.EnumerateObject())
        {
            if (!resource.Value.TryGetProperty("resource_type", out var type) || type.GetString() != "api_gateway")
                continue;

            foreach (var route in resource.Value.GetProperty("resources").EnumerateObject())
            {
                foreach (var method in route.Value.EnumerateObject())
                {
                    if (method.Value.ValueKind != JsonValueKind.Object ||
                        !method.Value.TryGetProperty("authorization_type", out var auth) ||
                        string.Equals(auth.GetString(), "NONE", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var pattern = "^" + Regex.Replace(Regex.Escape(route.Name), @"\\\{\w+}", "[^/]+") + "/?$";
                    _securedRoutes.Add((method.Name.ToUpperInvariant(), new Regex(pattern, RegexOptions.Compiled)));
                }
            }
        }

        logger.LogInformation("Loaded {Count} secured API routes", _securedRoutes.Count);
    }

    public bool RequiresAuthorization(string method, string path) =>
        _securedRoutes.Any(r => r.Method == method && r.Pattern.IsMatch(path));

    public Dictionary<string, string>? GetClaims(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
            return null;

        var token = authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorizationHeader[7..].Trim()
            : authorizationHeader.Trim();

        var payload = _jwt.Validate(token);
        if (payload is null)
            return null;

        var claims = new Dictionary<string, string>();
        foreach (var (name, value) in payload)
        {
            claims[name] = value switch
            {
                null => string.Empty,
                JsonArray array => string.Join(",", array.Select(v => v?.ToString())),
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                _ => value.ToJsonString(),
            };
        }

        if (!claims.ContainsKey("cognito:username") && claims.TryGetValue("username", out var username))
            claims["cognito:username"] = username;

        return claims;
    }
}
