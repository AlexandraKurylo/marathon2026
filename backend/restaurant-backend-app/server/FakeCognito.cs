using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RestaurantServer;

public sealed partial class FakeCognito
{
    private readonly object _lock = new();
    private readonly JwtService _jwt;
    private readonly string? _storeFile;
    private readonly Dictionary<string, StoredUser> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> _groups = new(StringComparer.Ordinal);

    public FakeCognito(JwtService jwt, ServerSettings settings)
    {
        _jwt = jwt;
        if (settings.DataDir is not null)
        {
            Directory.CreateDirectory(settings.DataDir);
            _storeFile = Path.Combine(settings.DataDir, "users.json");
            Load();
        }
    }

    public Task HandleAsync(HttpContext context) =>
        AwsJson.HandleAsync(context, "application/x-amz-json-1.1", Dispatch);

    public void AddUserToGroup(string username, string group)
    {
        lock (_lock)
        {
            var user = GetUser(username);
            _groups.Add(group);
            user.Groups.Add(group);
            Save();
        }
    }

    private JsonObject Dispatch(string operation, JsonObject input)
    {
        lock (_lock)
        {
            var result = operation switch
            {
                "SignUp" => SignUp(input),
                "AdminConfirmSignUp" => AdminConfirmSignUp(input),
                "InitiateAuth" or "AdminInitiateAuth" => InitiateAuth(input),
                "AdminGetUser" => AdminGetUser(input),
                "GetUser" => GetUserByToken(input),
                "ListUsers" => ListUsers(input),
                "ListGroups" => ListGroups(),
                "CreateGroup" => CreateGroup(input),
                "AdminAddUserToGroup" => ChangeGroup(input, add: true),
                "AdminRemoveUserFromGroup" => ChangeGroup(input, add: false),
                "AdminListGroupsForUser" => AdminListGroupsForUser(input),
                "AdminUpdateUserAttributes" => AdminUpdateUserAttributes(input),
                "ChangePassword" => ChangePassword(input),
                "AdminDeleteUser" => AdminDeleteUser(input),
                "AdminSetUserPassword" => AdminSetUserPassword(input),
                "GlobalSignOut" or "AdminUserGlobalSignOut" => new JsonObject(),
                _ => throw new AwsServiceException("InvalidAction", $"Operation {operation} is not supported by the local Cognito emulator."),
            };
            return result;
        }
    }

    private JsonObject SignUp(JsonObject input)
    {
        var username = Normalize(AwsJson.RequiredString(input, "Username"));
        var password = AwsJson.RequiredString(input, "Password");

        if (_users.ContainsKey(username))
            throw new AwsServiceException("UsernameExistsException", "An account with the given email already exists.");

        ValidatePasswordPolicy(password);

        var user = new StoredUser
        {
            Username = username,
            Sub = Guid.NewGuid().ToString(),
            PasswordHash = HashPassword(password),
            Confirmed = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var attribute in ReadAttributes(input["UserAttributes"]))
            user.Attributes[attribute.Key] = attribute.Value;

        user.Attributes["email"] = user.Attributes.TryGetValue("email", out var email) ? email.Trim().ToLowerInvariant() : username;
        _users[username] = user;
        Save();

        return new JsonObject { ["UserConfirmed"] = false, ["UserSub"] = user.Sub };
    }

    private JsonObject AdminConfirmSignUp(JsonObject input)
    {
        var user = GetUser(AwsJson.RequiredString(input, "Username"));
        user.Confirmed = true;
        user.Attributes["email_verified"] = "true";
        Save();
        return new JsonObject();
    }

    private JsonObject InitiateAuth(JsonObject input)
    {
        var parameters = input["AuthParameters"] as JsonObject ?? new JsonObject();
        var username = parameters["USERNAME"]?.GetValue<string>() ?? string.Empty;
        var password = parameters["PASSWORD"]?.GetValue<string>() ?? string.Empty;

        if (!_users.TryGetValue(Normalize(username), out var user) || !VerifyPassword(password, user.PasswordHash))
            throw new AwsServiceException("NotAuthorizedException", "Incorrect username or password.");

        if (!user.Confirmed)
            throw new AwsServiceException("UserNotConfirmedException", "User is not confirmed.");

        return new JsonObject
        {
            ["ChallengeParameters"] = new JsonObject(),
            ["AuthenticationResult"] = new JsonObject
            {
                ["AccessToken"] = CreateAccessToken(user),
                ["IdToken"] = CreateIdToken(user),
                ["RefreshToken"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["ExpiresIn"] = _jwt.LifetimeSeconds,
                ["TokenType"] = "Bearer",
            },
        };
    }

    private JsonObject AdminGetUser(JsonObject input)
    {
        var user = GetUser(AwsJson.RequiredString(input, "Username"));
        return new JsonObject
        {
            ["Username"] = user.Username,
            ["UserAttributes"] = AttributesJson(user),
            ["Enabled"] = true,
            ["UserStatus"] = user.Confirmed ? "CONFIRMED" : "UNCONFIRMED",
            ["UserCreateDate"] = user.CreatedAt.ToUnixTimeSeconds(),
            ["UserLastModifiedDate"] = user.CreatedAt.ToUnixTimeSeconds(),
        };
    }

    private JsonObject GetUserByToken(JsonObject input)
    {
        var user = GetUserFromAccessToken(AwsJson.RequiredString(input, "AccessToken"));
        return new JsonObject
        {
            ["Username"] = user.Username,
            ["UserAttributes"] = AttributesJson(user),
        };
    }

    private JsonObject ListUsers(JsonObject input)
    {
        IEnumerable<StoredUser> users = _users.Values.OrderBy(u => u.CreatedAt);

        var filter = AwsJson.OptionalString(input, "Filter");
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var match = FilterRegex().Match(filter);
            if (match.Success)
            {
                var attribute = match.Groups["name"].Value;
                var isPrefix = match.Groups["op"].Value == "^=";
                var value = match.Groups["value"].Value;
                users = users.Where(u =>
                {
                    var actual = attribute switch
                    {
                        "username" => u.Username,
                        "sub" => u.Sub,
                        _ => u.Attributes.GetValueOrDefault(attribute),
                    };
                    if (actual is null) return false;
                    return isPrefix
                        ? actual.StartsWith(value, StringComparison.OrdinalIgnoreCase)
                        : string.Equals(actual, value, StringComparison.OrdinalIgnoreCase);
                });
            }
        }

        var array = new JsonArray();
        foreach (var user in users)
        {
            array.Add(new JsonObject
            {
                ["Username"] = user.Username,
                ["Attributes"] = AttributesJson(user),
                ["Enabled"] = true,
                ["UserStatus"] = user.Confirmed ? "CONFIRMED" : "UNCONFIRMED",
                ["UserCreateDate"] = user.CreatedAt.ToUnixTimeSeconds(),
                ["UserLastModifiedDate"] = user.CreatedAt.ToUnixTimeSeconds(),
            });
        }

        return new JsonObject { ["Users"] = array };
    }

    private JsonObject ListGroups()
    {
        var array = new JsonArray();
        foreach (var group in _groups)
            array.Add(GroupJson(group));
        return new JsonObject { ["Groups"] = array };
    }

    private JsonObject CreateGroup(JsonObject input)
    {
        var name = AwsJson.RequiredString(input, "GroupName");
        if (!_groups.Add(name))
            throw new AwsServiceException("GroupExistsException", "A group with the name already exists.");
        Save();
        return new JsonObject { ["Group"] = GroupJson(name) };
    }

    private JsonObject ChangeGroup(JsonObject input, bool add)
    {
        var user = GetUser(AwsJson.RequiredString(input, "Username"));
        var group = AwsJson.RequiredString(input, "GroupName");
        if (add)
        {
            _groups.Add(group);
            user.Groups.Add(group);
        }
        else
        {
            user.Groups.Remove(group);
        }
        Save();
        return new JsonObject();
    }

    private JsonObject AdminListGroupsForUser(JsonObject input)
    {
        var user = GetUser(AwsJson.RequiredString(input, "Username"));
        var array = new JsonArray();
        foreach (var group in user.Groups.OrderBy(g => g, StringComparer.Ordinal))
            array.Add(GroupJson(group));
        return new JsonObject { ["Groups"] = array };
    }

    private JsonObject AdminUpdateUserAttributes(JsonObject input)
    {
        var user = GetUser(AwsJson.RequiredString(input, "Username"));
        foreach (var attribute in ReadAttributes(input["UserAttributes"]))
        {
            if (attribute.Key is "sub")
                continue;
            user.Attributes[attribute.Key] = attribute.Value;
        }
        Save();
        return new JsonObject();
    }

    private JsonObject ChangePassword(JsonObject input)
    {
        var user = GetUserFromAccessToken(AwsJson.RequiredString(input, "AccessToken"));
        var previous = AwsJson.RequiredString(input, "PreviousPassword");
        var proposed = AwsJson.RequiredString(input, "ProposedPassword");

        if (!VerifyPassword(previous, user.PasswordHash))
            throw new AwsServiceException("NotAuthorizedException", "Incorrect username or password.");

        ValidatePasswordPolicy(proposed);
        user.PasswordHash = HashPassword(proposed);
        Save();
        return new JsonObject();
    }

    private JsonObject AdminSetUserPassword(JsonObject input)
    {
        var user = GetUser(AwsJson.RequiredString(input, "Username"));
        var password = AwsJson.RequiredString(input, "Password");
        ValidatePasswordPolicy(password);
        user.PasswordHash = HashPassword(password);
        user.Confirmed = true;
        Save();
        return new JsonObject();
    }

    private JsonObject AdminDeleteUser(JsonObject input)
    {
        var user = GetUser(AwsJson.RequiredString(input, "Username"));
        _users.Remove(user.Username);
        Save();
        return new JsonObject();
    }

    private StoredUser GetUser(string username) =>
        _users.TryGetValue(Normalize(username), out var user)
            ? user
            : _users.Values.FirstOrDefault(u => u.Sub == username)
              ?? throw new AwsServiceException("UserNotFoundException", "User does not exist.");

    private StoredUser GetUserFromAccessToken(string token)
    {
        var claims = _jwt.Validate(token)
            ?? throw new AwsServiceException("NotAuthorizedException", "Invalid Access Token");
        var sub = claims["sub"]?.GetValue<string>();
        return _users.Values.FirstOrDefault(u => u.Sub == sub)
            ?? throw new AwsServiceException("NotAuthorizedException", "Invalid Access Token");
    }

    private string CreateIdToken(StoredUser user)
    {
        var claims = new JsonObject
        {
            ["sub"] = user.Sub,
            ["aud"] = ServerSettings.ClientId,
            ["token_use"] = "id",
            ["cognito:username"] = user.Username,
            ["email"] = user.Attributes.GetValueOrDefault("email", user.Username),
            ["email_verified"] = true,
            ["given_name"] = user.Attributes.GetValueOrDefault("given_name", string.Empty),
            ["family_name"] = user.Attributes.GetValueOrDefault("family_name", string.Empty),
        };
        if (user.Groups.Count > 0)
            claims["cognito:groups"] = new JsonArray(user.Groups.Select(g => (JsonNode)JsonValue.Create(g)!).ToArray());
        return _jwt.Create(claims);
    }

    private string CreateAccessToken(StoredUser user)
    {
        var claims = new JsonObject
        {
            ["sub"] = user.Sub,
            ["client_id"] = ServerSettings.ClientId,
            ["token_use"] = "access",
            ["scope"] = "aws.cognito.signin.user.admin",
            ["username"] = user.Username,
        };
        if (user.Groups.Count > 0)
            claims["cognito:groups"] = new JsonArray(user.Groups.Select(g => (JsonNode)JsonValue.Create(g)!).ToArray());
        return _jwt.Create(claims);
    }

    private static JsonArray AttributesJson(StoredUser user)
    {
        var array = new JsonArray { new JsonObject { ["Name"] = "sub", ["Value"] = user.Sub } };
        foreach (var (name, value) in user.Attributes)
            array.Add(new JsonObject { ["Name"] = name, ["Value"] = value });
        return array;
    }

    private static JsonObject GroupJson(string name) => new()
    {
        ["GroupName"] = name,
        ["UserPoolId"] = ServerSettings.UserPoolId,
        ["Description"] = $"{name} group",
    };

    private static IEnumerable<KeyValuePair<string, string>> ReadAttributes(JsonNode? node)
    {
        if (node is not JsonArray array)
            yield break;

        foreach (var item in array.OfType<JsonObject>())
        {
            var name = item["Name"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(name))
                yield return new(name, item["Value"]?.GetValue<string>() ?? string.Empty);
        }
    }

    private static void ValidatePasswordPolicy(string password)
    {
        var problems = new List<string>();
        if (password.Length < 8) problems.Add("Password not long enough");
        if (!password.Any(char.IsLower)) problems.Add("Password must have lowercase characters");
        if (!password.Any(char.IsUpper)) problems.Add("Password must have uppercase characters");
        if (!password.Any(char.IsDigit)) problems.Add("Password must have numeric characters");
        if (password.All(char.IsLetterOrDigit)) problems.Add("Password must have symbol characters");

        if (problems.Count > 0)
            throw new AwsServiceException("InvalidPasswordException", $"Password did not conform with policy: {problems[0]}");
    }

    private static string Normalize(string username) => username.Trim().ToLowerInvariant();

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private void Load()
    {
        if (_storeFile is null || !File.Exists(_storeFile))
            return;

        var state = JsonSerializer.Deserialize<StoreState>(File.ReadAllText(_storeFile));
        if (state is null)
            return;

        foreach (var user in state.Users)
            _users[user.Username] = user;
        foreach (var group in state.Groups)
            _groups.Add(group);
    }

    private void Save()
    {
        if (_storeFile is null)
            return;

        var state = new StoreState { Users = _users.Values.ToList(), Groups = _groups.ToList() };
        File.WriteAllText(_storeFile, JsonSerializer.Serialize(state));
    }

    [GeneratedRegex("^\\s*(?<name>[\\w:]+)\\s*(?<op>\\^?=)\\s*\"(?<value>[^\"]*)\"\\s*$")]
    private static partial Regex FilterRegex();

    private sealed class StoreState
    {
        public List<StoredUser> Users { get; set; } = [];
        public List<string> Groups { get; set; } = [];
    }

    private sealed class StoredUser
    {
        public string Username { get; set; } = string.Empty;
        public string Sub { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public bool Confirmed { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = new();
        public HashSet<string> Groups { get; set; } = new(StringComparer.Ordinal);
    }
}
