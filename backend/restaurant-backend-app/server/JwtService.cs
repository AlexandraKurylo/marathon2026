using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RestaurantServer;

public sealed class JwtService(string secret)
{
    public const string Issuer = "restaurant-local-auth";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    public int LifetimeSeconds => (int)Lifetime.TotalSeconds;

    public string Create(JsonObject claims)
    {
        var now = DateTimeOffset.UtcNow;
        claims["iss"] = Issuer;
        claims["iat"] = now.ToUnixTimeSeconds();
        claims["auth_time"] = now.ToUnixTimeSeconds();
        claims["exp"] = now.Add(Lifetime).ToUnixTimeSeconds();
        claims["jti"] = Guid.NewGuid().ToString();

        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var payload = Base64Url(Encoding.UTF8.GetBytes(claims.ToJsonString()));
        var signature = Base64Url(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{signature}";
    }

    public JsonObject? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        try
        {
            var expected = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));
            if (!CryptographicOperations.FixedTimeEquals(expected, FromBase64Url(parts[2])))
                return null;

            var claims = JsonNode.Parse(FromBase64Url(parts[1])) as JsonObject;
            if (claims is null || claims["iss"]?.GetValue<string>() != Issuer)
                return null;

            var exp = claims["exp"]?.GetValue<long>() ?? 0;
            return exp > DateTimeOffset.UtcNow.ToUnixTimeSeconds() ? claims : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
