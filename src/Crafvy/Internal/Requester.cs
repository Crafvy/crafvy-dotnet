using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Crafvy.Internal;

/// <summary>Sends requests: headers, retries, timeouts, errors — what every generated method calls.</summary>
internal sealed class Requester
{
    private static readonly HashSet<string> Idempotent = new() { "GET", "HEAD", "PUT", "DELETE" };
    private static readonly HashSet<int> Retry5xx = new() { 500, 502, 503, 504 };
    private static readonly Random Jitter = new();

    private readonly HttpClient _http;
    private readonly string? _token;
    private readonly string _baseUrl;
    private readonly TimeSpan _timeout;
    private readonly int _retries;
    private readonly string _userAgent;

    internal Requester(HttpClient http, string? token, string baseUrl, TimeSpan timeout, int retries, string userAgent)
    {
        _http = http;
        _token = token;
        _baseUrl = baseUrl;
        _timeout = timeout;
        _retries = retries;
        _userAgent = userAgent;
    }

    /// <summary>A path parameter, URL-encoded: everything but A–Z a–z 0–9 - . _ ~.</summary>
    internal static string Seg(string value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value), "a path parameter is null");
        return Uri.EscapeDataString(value);
    }

    internal async Task<T> CallAsync<T>(string method, string path, object? query, object? body, bool multipart, string? returns, RequestOptions? options, CancellationToken cancellationToken)
    {
        var answer = await SendAsync(method, path, query, body, multipart, options, cancellationToken).ConfigureAwait(false);
        if (returns != null && answer is { } a)
        {
            JsonElement? at = a;
            foreach (var key in returns.Split('.'))
            {
                if (at is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(key, out var next)) at = next;
                else
                {
                    at = null;
                    break;
                }
            }
            answer = at;
        }
        if (answer is not { } value || value.ValueKind == JsonValueKind.Null) return default!;
        try
        {
            return value.Deserialize<T>(Json.Options)!;
        }
        catch (JsonException e)
        {
            throw new CrafvyException(200, null, $"the answer of {method} {path} could not be decoded: {e.Message}", value, e);
        }
    }

    internal async Task<JsonElement?> SendAsync(string method, string path, object? query, object? body, bool multipart, RequestOptions? options, CancellationToken cancellationToken)
    {
        var limit = options?.Timeout ?? _timeout;
        var retries = Math.Max(0, options?.Retries ?? _retries);
        var qs = QueryString(query);
        var url = _baseUrl + (path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path) + (qs.Length > 0 ? "?" + qs : "");
        byte[]? payload = null;
        Func<HttpContent>? multipartContent = null;
        if (body != null)
        {
            if (multipart) multipartContent = () => Multipart(body);
            else payload = JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), Json.Options);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (limit > TimeSpan.Zero && limit != Timeout.InfiniteTimeSpan) cts.CancelAfter(limit);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var req = new HttpRequestMessage(new HttpMethod(method), url);
                if (_token != null) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                req.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
                if (payload != null)
                {
                    req.Content = new ByteArrayContent(payload);
                    req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                }
                else if (multipartContent != null)
                {
                    req.Content = multipartContent();
                }
                if (options != null)
                {
                    foreach (var h in options.Headers)
                    {
                        req.Headers.Remove(h.Key);
                        req.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    }
                }

                HttpResponseMessage res;
                try
                {
                    res = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    throw new CrafvyException(0, null, $"{method} {path}: {e.Message}", null, e);
                }
                using (res)
                {
                    var status = (int)res.StatusCode;
#if NET5_0_OR_GREATER
                    var raw = await res.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
#else
                    var raw = await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
                    var retryAfter = RetryAfter(res.Headers.RetryAfter);
                    var retryable = status == 429 || (Idempotent.Contains(method) && Retry5xx.Contains(status));
                    if (retryable && attempt < retries)
                    {
                        await Task.Delay(retryAfter ?? Backoff(attempt), cts.Token).ConfigureAwait(false);
                        continue;
                    }
                    if (status >= 400) throw CrafvyException.From(status, Json.Parse(raw), retryAfter);
                    return Json.Parse(raw);
                }
            }
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CrafvyException(408, null, $"request timed out after {limit}", null, e);
        }
    }

    private static TimeSpan Backoff(int attempt)
    {
        int jitter;
        lock (Jitter) jitter = Jitter.Next(100);
        return TimeSpan.FromMilliseconds(500 * Math.Pow(2, Math.Min(attempt, 10)) + jitter);
    }

    private static TimeSpan? RetryAfter(RetryConditionHeaderValue? v)
    {
        if (v == null) return null;
        if (v.Delta is { } d) return d < TimeSpan.Zero ? TimeSpan.Zero : d;
        if (v.Date is { } at)
        {
            var wait = at - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }
        return null;
    }

    /// <summary>
    /// A query (model, dictionary or anonymous object) as an encoded query string: absent values
    /// left out, booleans true/false, a structured value (object, list) as JSON text.
    /// </summary>
    internal static string QueryString(object? query)
    {
        if (query == null) return "";
        var tree = JsonSerializer.SerializeToElement(query, query.GetType(), Json.Options);
        if (tree.ValueKind != JsonValueKind.Object) throw new ArgumentException("a query must be a query model, a dictionary or an anonymous object", nameof(query));
        var sb = new StringBuilder();
        foreach (var p in tree.EnumerateObject())
        {
            var text = Json.Text(p.Value);
            if (text == null) continue;
            if (sb.Length > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(p.Name)).Append('=').Append(Uri.EscapeDataString(text));
        }
        return sb.ToString();
    }

    /// <summary>
    /// A body model (or dictionary) as multipart/form-data: a <see cref="CrafvyFile"/> (or a list of
    /// them) as file parts, everything else as text fields.
    /// </summary>
    internal static MultipartFormDataContent Multipart(object body)
    {
        var fields = new List<KeyValuePair<string, object?>>();
        if (body is IDictionary dict)
        {
            foreach (DictionaryEntry e in dict) fields.Add(new KeyValuePair<string, object?>(Convert.ToString(e.Key, System.Globalization.CultureInfo.InvariantCulture) ?? "", e.Value));
        }
        else
        {
            foreach (var p in body.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || p.GetCustomAttribute<JsonExtensionDataAttribute>() != null) continue;
                var name = p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name;
                fields.Add(new KeyValuePair<string, object?>(name, p.GetValue(body)));
            }
        }
        var content = new MultipartFormDataContent("crafvy-" + Guid.NewGuid().ToString("N"));
        foreach (var f in fields)
        {
            switch (f.Value)
            {
                case null:
                    continue;
                case CrafvyFile file:
                    content.Add(FilePart(f.Key, file));
                    continue;
                case IEnumerable<CrafvyFile> files:
                    foreach (var file in files.Where(x => x != null)) content.Add(FilePart(f.Key, file));
                    continue;
            }
            var text = Json.Text(JsonSerializer.SerializeToElement(f.Value, f.Value.GetType(), Json.Options));
            if (text == null) continue;
            var part = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
            part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = Quote(f.Key) };
            content.Add(part);
        }
        return content;
    }

    private static ByteArrayContent FilePart(string field, CrafvyFile file)
    {
        var part = new ByteArrayContent(file.Data);
        part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = Quote(field),
            FileName = Quote(string.IsNullOrEmpty(file.Name) ? field : file.Name!),
        };
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType!);
        return part;
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "") + "\"";
}
