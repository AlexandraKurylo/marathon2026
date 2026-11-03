using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using APIHandler.Helpers;
using System.Diagnostics;

namespace RestaurantServer;

public sealed class DynamoDbLocal(ServerSettings settings, ILogger<DynamoDbLocal> logger) : IDisposable
{
    private Process? _process;

    private sealed record IndexSpec(string Name, string HashKey, string HashType, string? RangeKey = null, string? RangeType = null);

    private sealed record TableSpec(string Name, string HashKey, string HashType, string? RangeKey = null, string? RangeType = null, params IndexSpec[] Indexes);

    private static readonly TableSpec[] Tables =
    [
        new("Waiters", "userId", "S", null, null, new IndexSpec("locationId-index", "locationId", "S")),
        new("Feedback", "reservationId", "S", "createdAt", "S", new IndexSpec("locationId-createdAt-index", "locationId", "S", "createdAt", "S")),
        new("Tables", "id", "S", null, null, new IndexSpec("locationId-tableNumber-index", "locationId", "S", "tableNumber", "N")),
        new("Dishes", "id", "S"),
        new("Reservations", "id", "S", null, null,
            new IndexSpec("GSI_ByTableId", "tableId", "S"),
            new IndexSpec("GSI_WaiterId", "waiterId", "S"),
            new IndexSpec("GSI_UserId", "customerId", "S")),
        new("Locations", "id", "S"),
        new("ProfileImages", "userId", "S"),
        new("Orders", "orderId", "S", "customerId", "S",
            new IndexSpec("GSI_CustomerId", "customerId", "S"),
            new IndexSpec("GSI_ReservationId", "reservationId", "S")),
        new("Reports", "waiterId", "S", "date", "S"),
        new("ReportsHistory", "id", "S", null, null, new IndexSpec("UserIdIndex", "userId", "S")),
        new("Notifications", "id", "S", null, null, new IndexSpec("UserIdIndex", "userId", "S")),
        new("GroupReservations", "groupReservationId", "S", null, null,
            new IndexSpec("reservationToken", "reservationToken", "S"),
            new IndexSpec("reservationId", "reservationId", "S")),
        new("GuestSessions", "guestSessionId", "S", null, null,
            new IndexSpec("groupReservationId", "groupReservationId", "S"),
            new IndexSpec("guestSessionTokenHash", "guestSessionTokenHash", "S")),
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (settings.StartDynamoDbLocal)
            StartProcess();

        using var client = AwsClientFactory.CreateDynamoDb();
        await WaitUntilReadyAsync(client, cancellationToken);
        await CreateTablesAsync(client, cancellationToken);
    }

    private void StartProcess()
    {
        var dir = settings.DynamoDbLocalDir;
        var jar = Path.Combine(dir, "DynamoDBLocal.jar");
        if (!File.Exists(jar))
            throw new FileNotFoundException($"DynamoDB Local was not found at {jar}. Set DYNAMODB_URL to use an external DynamoDB Local instead.");

        var port = new Uri(settings.DynamoDbUrl).Port;
        var start = new ProcessStartInfo("java")
        {
            WorkingDirectory = dir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in (Environment.GetEnvironmentVariable("DYNAMODB_JAVA_OPTS") ?? "-Xms32m -Xmx128m -XX:MaxMetaspaceSize=96m -XX:+UseSerialGC -XX:TieredStopAtLevel=1 -Xss512k")
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries))
            start.ArgumentList.Add(arg);

        start.ArgumentList.Add($"-Djava.library.path={Path.Combine(dir, "DynamoDBLocal_lib")}");
        start.ArgumentList.Add("-jar");
        start.ArgumentList.Add(jar);
        start.ArgumentList.Add("-sharedDb");
        start.ArgumentList.Add("-port");
        start.ArgumentList.Add(port.ToString());

        if (settings.DataDir is not null)
        {
            var dbDir = Path.Combine(settings.DataDir, "dynamodb");
            Directory.CreateDirectory(dbDir);
            start.ArgumentList.Add("-dbPath");
            start.ArgumentList.Add(dbDir);
        }
        else
        {
            start.ArgumentList.Add("-inMemory");
        }

        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start DynamoDB Local.");
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) logger.LogInformation("[dynamodb] {Line}", e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) logger.LogWarning("[dynamodb] {Line}", e.Data); };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        logger.LogInformation("DynamoDB Local started on port {Port}", port);
    }

    private async Task WaitUntilReadyAsync(IAmazonDynamoDB client, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            try
            {
                await client.ListTablesAsync(new ListTablesRequest(), cancellationToken);
                return;
            }
            catch (Exception ex) when (DateTime.UtcNow < deadline && ex is not OperationCanceledException)
            {
                if (_process is { HasExited: true })
                    throw new InvalidOperationException($"DynamoDB Local exited with code {_process.ExitCode}.");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task CreateTablesAsync(IAmazonDynamoDB client, CancellationToken cancellationToken)
    {
        var existing = (await client.ListTablesAsync(new ListTablesRequest(), cancellationToken)).TableNames ?? [];

        foreach (var table in Tables)
        {
            if (existing.Contains(table.Name))
                continue;

            var attributes = new Dictionary<string, string> { [table.HashKey] = table.HashType };
            var keys = new List<KeySchemaElement> { new(table.HashKey, KeyType.HASH) };
            if (table.RangeKey is not null)
            {
                attributes[table.RangeKey] = table.RangeType!;
                keys.Add(new KeySchemaElement(table.RangeKey, KeyType.RANGE));
            }

            var indexes = new List<GlobalSecondaryIndex>();
            foreach (var index in table.Indexes)
            {
                attributes[index.HashKey] = index.HashType;
                var indexKeys = new List<KeySchemaElement> { new(index.HashKey, KeyType.HASH) };
                if (index.RangeKey is not null)
                {
                    attributes[index.RangeKey] = index.RangeType!;
                    indexKeys.Add(new KeySchemaElement(index.RangeKey, KeyType.RANGE));
                }

                indexes.Add(new GlobalSecondaryIndex
                {
                    IndexName = index.Name,
                    KeySchema = indexKeys,
                    Projection = new Projection { ProjectionType = ProjectionType.ALL },
                });
            }

            var request = new CreateTableRequest
            {
                TableName = table.Name,
                KeySchema = keys,
                AttributeDefinitions = attributes
                    .Select(a => new AttributeDefinition(a.Key, a.Value == "N" ? ScalarAttributeType.N : ScalarAttributeType.S))
                    .ToList(),
                BillingMode = BillingMode.PAY_PER_REQUEST,
            };
            if (indexes.Count > 0)
                request.GlobalSecondaryIndexes = indexes;

            await client.CreateTableAsync(request, cancellationToken);
            logger.LogInformation("Created table {Table}", table.Name);
        }
    }

    public void Dispose()
    {
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
        _process?.Dispose();
    }
}
