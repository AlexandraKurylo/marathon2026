using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace RestaurantServer;

public sealed class FakeS3
{
    private readonly ConcurrentDictionary<string, StoredObject> _objects = new(StringComparer.Ordinal);
    private readonly string? _storeDir;
    private readonly HashSet<string> _buckets;

    public FakeS3(ServerSettings settings)
    {
        _buckets = new HashSet<string>(StringComparer.Ordinal) { ServerSettings.ImagesBucket, ServerSettings.ProfileImagesBucket };
        if (settings.DataDir is not null)
        {
            _storeDir = Path.Combine(settings.DataDir, "s3");
            Directory.CreateDirectory(_storeDir);
        }
    }

    public bool IsBucketPath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        var segment = value.TrimStart('/').Split('/', 2)[0];
        return _buckets.Contains(segment);
    }

    public async Task HandleAsync(HttpContext context)
    {
        var parts = (context.Request.Path.Value ?? string.Empty).TrimStart('/').Split('/', 2);
        var bucket = parts[0];
        var key = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        var id = $"{bucket}/{key}";

        context.Response.Headers["Access-Control-Allow-Origin"] = "*";

        switch (context.Request.Method)
        {
            case "PUT" when key.Length > 0:
                var data = await ReadBodyAsync(context.Request);
                var stored = new StoredObject(data, context.Request.ContentType ?? "application/octet-stream");
                _objects[id] = stored;
                await PersistAsync(id, stored);
                context.Response.Headers.ETag = $"\"{stored.ETag}\"";
                context.Response.StatusCode = 200;
                return;

            case "GET" or "HEAD" when key.Length > 0:
                var found = Find(id);
                if (found is null)
                {
                    await WriteErrorAsync(context, 404, "NoSuchKey", "The specified key does not exist.");
                    return;
                }
                context.Response.StatusCode = 200;
                context.Response.ContentType = found.ContentType;
                context.Response.ContentLength = found.Data.Length;
                context.Response.Headers.ETag = $"\"{found.ETag}\"";
                context.Response.Headers.CacheControl = "public, max-age=3600";
                if (context.Request.Method == "GET")
                    await context.Response.Body.WriteAsync(found.Data);
                return;

            case "DELETE" when key.Length > 0:
                _objects.TryRemove(id, out _);
                if (_storeDir is not null)
                {
                    var file = FilePath(id);
                    if (File.Exists(file)) File.Delete(file);
                    if (File.Exists(file + ".type")) File.Delete(file + ".type");
                }
                context.Response.StatusCode = 204;
                return;

            default:
                context.Response.StatusCode = 200;
                return;
        }
    }

    private StoredObject? Find(string id)
    {
        if (_objects.TryGetValue(id, out var obj))
            return obj;

        if (_storeDir is null)
            return null;

        var file = FilePath(id);
        if (!File.Exists(file))
            return null;

        var contentType = File.Exists(file + ".type") ? File.ReadAllText(file + ".type") : "application/octet-stream";
        obj = new StoredObject(File.ReadAllBytes(file), contentType);
        _objects[id] = obj;
        return obj;
    }

    private async Task PersistAsync(string id, StoredObject obj)
    {
        if (_storeDir is null)
            return;

        var file = FilePath(id);
        await File.WriteAllBytesAsync(file, obj.Data);
        await File.WriteAllTextAsync(file + ".type", obj.ContentType);
    }

    private string FilePath(string id) =>
        Path.Combine(_storeDir!, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id))));

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer);
        var raw = buffer.ToArray();

        var contentSha = request.Headers["x-amz-content-sha256"].ToString();
        var encoding = request.Headers.ContentEncoding.ToString();
        var isChunked = contentSha.StartsWith("STREAMING-", StringComparison.OrdinalIgnoreCase)
                        || encoding.Contains("aws-chunked", StringComparison.OrdinalIgnoreCase);

        return isChunked ? DecodeAwsChunked(raw) : raw;
    }

    private static byte[] DecodeAwsChunked(byte[] raw)
    {
        using var output = new MemoryStream();
        var position = 0;
        while (position < raw.Length)
        {
            var lineEnd = IndexOfCrLf(raw, position);
            if (lineEnd < 0)
                break;

            var header = Encoding.ASCII.GetString(raw, position, lineEnd - position);
            var sizeText = header.Split(';')[0].Trim();
            if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, null, out var size) || size == 0)
                break;

            position = lineEnd + 2;
            output.Write(raw, position, Math.Min(size, raw.Length - position));
            position += size + 2;
        }
        return output.ToArray();
    }

    private static int IndexOfCrLf(byte[] data, int start)
    {
        for (var i = start; i < data.Length - 1; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n')
                return i;
        }
        return -1;
    }

    private static async Task WriteErrorAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/xml";
        if (context.Request.Method != "HEAD")
        {
            await context.Response.WriteAsync(
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>{code}</Code><Message>{message}</Message><RequestId>{Guid.NewGuid():N}</RequestId></Error>");
        }
    }

    private sealed record StoredObject(byte[] Data, string ContentType)
    {
        public string ETag { get; } = Convert.ToHexStringLower(MD5.HashData(Data));
    }
}
