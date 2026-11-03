namespace RestaurantServer;

public sealed class ServerSettings
{
    public const string UserPoolId = "local_userpool";
    public const string ClientId = "local-client";
    public const string ImagesBucket = "restaurant-images";
    public const string ProfileImagesBucket = "restaurant-profile-images";
    public const string ReportsQueue = "restaurant-reports-queue";

    private static readonly Dictionary<string, string> LambdaEnvironment = new()
    {
        ["REGION"] = "us-west-2",
        ["COGNITO_USER_POOL_ID"] = UserPoolId,
        ["COGNITO_USER_POOL_CLIENT_ID"] = ClientId,
        ["reports_queue_name"] = ReportsQueue,
        ["OrderIndexReservationId"] = "GSI_ReservationId",
        ["notificationsTable"] = "Notifications",
        ["FEEDBACK_TABLE_NAME"] = "Feedback",
        ["locationsTable"] = "Locations",
        ["WaiterListTableName"] = "Waiters",
        ["tablesTable"] = "Tables",
        ["dishesTable"] = "Dishes",
        ["reservationsTable"] = "Reservations",
        ["reportsTable"] = "Reports",
        ["reportsHistoryTable"] = "ReportsHistory",
        ["profileImagesTable"] = "ProfileImages",
        ["notificationsUserIdIndex"] = "UserIdIndex",
        ["reservationsCustomerIdGlobalIndex"] = "GSI_UserId",
        ["reservationsWaiterIdGlobalIndex"] = "GSI_WaiterId",
        ["reservationsTableGlobalIndex"] = "GSI_ByTableId",
        ["waitersGlobalIndex"] = "locationId-index",
        ["imagesBucketName"] = ImagesBucket,
        ["profileImagesBucketName"] = ProfileImagesBucket,
        ["ordersTable"] = "Orders",
        ["feedbackGlobalIndex"] = "locationId-createdAt-index",
        ["groupReservationsTable"] = "GroupReservations",
        ["guestSessionTable"] = "GuestSessions",
        ["tableLocationIdIndex"] = "locationId-tableNumber-index",
    };

    public required int Port { get; init; }
    public required string InternalUrl { get; init; }
    public required string PublicUrl { get; init; }
    public required string DynamoDbUrl { get; init; }
    public required bool StartDynamoDbLocal { get; init; }
    public required string DynamoDbLocalDir { get; init; }
    public required string? DataDir { get; init; }
    public required string JwtSecret { get; init; }
    public required bool Seed { get; init; }
    public required string ApiResourcesFile { get; init; }

    public static ServerSettings Load()
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 8080;
        var internalUrl = $"http://127.0.0.1:{port}";
        var publicUrl = (FirstNonEmpty(
            Environment.GetEnvironmentVariable("PUBLIC_URL"),
            Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL")) ?? $"http://localhost:{port}").TrimEnd('/');

        var externalDynamo = Environment.GetEnvironmentVariable("DYNAMODB_URL");
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR");

        return new ServerSettings
        {
            Port = port,
            InternalUrl = internalUrl,
            PublicUrl = publicUrl,
            DynamoDbUrl = string.IsNullOrWhiteSpace(externalDynamo) ? "http://127.0.0.1:8000" : externalDynamo.TrimEnd('/'),
            StartDynamoDbLocal = string.IsNullOrWhiteSpace(externalDynamo),
            DynamoDbLocalDir = Environment.GetEnvironmentVariable("DYNAMODB_LOCAL_DIR") ?? "/opt/dynamodb",
            DataDir = string.IsNullOrWhiteSpace(dataDir) ? null : dataDir,
            JwtSecret = FirstNonEmpty(Environment.GetEnvironmentVariable("JWT_SECRET")) ?? "local-development-secret-change-me-please-32b",
            Seed = !string.Equals(Environment.GetEnvironmentVariable("SEED_DATA"), "false", StringComparison.OrdinalIgnoreCase),
            ApiResourcesFile = Environment.GetEnvironmentVariable("API_RESOURCES_FILE")
                ?? Path.Combine(AppContext.BaseDirectory, "deployment_resources.json"),
        };
    }

    public void ApplyLambdaEnvironment()
    {
        foreach (var (key, value) in LambdaEnvironment)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }

        Environment.SetEnvironmentVariable("LOCAL_DYNAMODB_ENDPOINT", DynamoDbUrl);
        Environment.SetEnvironmentVariable("LOCAL_AWS_ENDPOINT", InternalUrl);
        Environment.SetEnvironmentVariable("S3_PUBLIC_URL", PublicUrl);
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "local");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "local");
        Environment.SetEnvironmentVariable("AWS_REGION", "us-west-2");
        Environment.SetEnvironmentVariable("AWS_EC2_METADATA_DISABLED", "true");
    }

    public string RewriteInternalUrls(string body) =>
        body.Contains(InternalUrl, StringComparison.Ordinal)
            ? body.Replace(InternalUrl, PublicUrl, StringComparison.Ordinal)
            : body;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
