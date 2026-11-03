using RestaurantServer;

var settings = ServerSettings.Load();
settings.ApplyLambdaEnvironment();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{settings.Port}");
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 20 * 1024 * 1024);
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(new JwtService(settings.JwtSecret));
builder.Services.AddSingleton<FakeCognito>();
builder.Services.AddSingleton<FakeSqs>();
builder.Services.AddSingleton<FakeS3>();
builder.Services.AddSingleton<ApiAuthorizer>();
builder.Services.AddSingleton<LambdaBridge>();
builder.Services.AddSingleton<DynamoDbLocal>();
builder.Services.AddSingleton<DataSeeder>();

var app = builder.Build();

var ready = false;
var cognito = app.Services.GetRequiredService<FakeCognito>();
var sqs = app.Services.GetRequiredService<FakeSqs>();
var s3 = app.Services.GetRequiredService<FakeS3>();
var bridge = app.Services.GetRequiredService<LambdaBridge>();

app.Run(async context =>
{
    var target = context.Request.Headers["X-Amz-Target"].ToString();
    if (target.StartsWith("AWSCognitoIdentityProviderService.", StringComparison.Ordinal))
    {
        await cognito.HandleAsync(context);
        return;
    }

    if (target.StartsWith("AmazonSQS.", StringComparison.Ordinal))
    {
        await sqs.HandleAsync(context);
        return;
    }

    if (s3.IsBucketPath(context.Request.Path))
    {
        await s3.HandleAsync(context);
        return;
    }

    if (context.Request.Path == "/health" || context.Request.Path == "/")
    {
        Cors.Apply(context.Response);
        context.Response.StatusCode = ready ? 200 : 503;
        await context.Response.WriteAsJsonAsync(new { status = ready ? "ok" : "starting", service = "restaurant-backend" });
        return;
    }

    if (HttpMethods.IsOptions(context.Request.Method))
    {
        Cors.Apply(context.Response);
        context.Response.StatusCode = 204;
        return;
    }

    if (!ready)
    {
        Cors.Apply(context.Response);
        context.Response.StatusCode = 503;
        await context.Response.WriteAsJsonAsync(new { message = "Server is starting, try again in a few seconds." });
        return;
    }

    await bridge.HandleAsync(context);
});

await app.StartAsync();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
try
{
    await app.Services.GetRequiredService<DynamoDbLocal>().StartAsync(app.Lifetime.ApplicationStopping);
    if (settings.Seed)
        await app.Services.GetRequiredService<DataSeeder>().SeedAsync(app.Lifetime.ApplicationStopping);
    ready = true;
    logger.LogInformation("Restaurant backend is ready at {Url}", settings.PublicUrl);
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Startup failed");
    await app.StopAsync();
    throw;
}

await app.WaitForShutdownAsync();
