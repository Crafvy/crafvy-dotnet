using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crafvy.Internal;

namespace Crafvy;

/// <summary>
/// The client of the Crafvy API.
/// <code>
/// var crafvy = new CrafvyClient(Environment.GetEnvironmentVariable("CRAFVY_TOKEN"));
/// var projects = await crafvy.Projects.ListAsync();
/// var record = await crafvy.Crm.Records.CreateAsync(spaceId, new CrmRecordsCreateBody { Data = new() { ["name"] = "Anna" } });
/// </code>
/// </summary>
/// <remarks>
/// Resources are properties (<c>crafvy.Crm.Records</c>); every method takes the path
/// parameters, then the body, then the query, then optional <see cref="RequestOptions"/>
/// and a <see cref="CancellationToken"/>. A token acts as its owner: their roles,
/// workspaces, feature gates and plan limits apply exactly as in the dashboard.
/// The client is thread-safe; share one.
/// </remarks>
public sealed partial class CrafvyClient : IDisposable
{
    /// <summary>Where the API answers unless a base URL or CRAFVY_BASE_URL says otherwise.</summary>
    public const string DefaultBaseUrl = "https://app.crafvy.com";

    /// <summary>This SDK's version.</summary>
    public static readonly string Version = ReadVersion();

    private readonly Requester _requester;
    private readonly HttpClient? _ownedHttp;

    /// <summary>A client for the token; null or empty reads CRAFVY_TOKEN.</summary>
    public CrafvyClient(string? token = null, CrafvyClientOptions? options = null)
    {
        options ??= new CrafvyClientOptions();
        HttpClient http;
        if (options.HttpClient != null)
        {
            http = options.HttpClient;
        }
        else
        {
            http = options.HttpMessageHandler != null ? new HttpClient(options.HttpMessageHandler, disposeHandler: false) : new HttpClient();
            http.Timeout = Timeout.InfiniteTimeSpan;
            _ownedHttp = http;
        }
        var resolved = string.IsNullOrEmpty(token) ? Environment.GetEnvironmentVariable("CRAFVY_TOKEN") : token;
        var baseUrl = options.BaseUrl;
        if (string.IsNullOrEmpty(baseUrl)) baseUrl = Environment.GetEnvironmentVariable("CRAFVY_BASE_URL");
        if (string.IsNullOrEmpty(baseUrl)) baseUrl = DefaultBaseUrl;
        BaseUrl = baseUrl!.TrimEnd('/');
        _requester = new Requester(http, string.IsNullOrEmpty(resolved) ? null : resolved, BaseUrl, options.Timeout, Math.Max(0, options.Retries), "crafvy-dotnet/" + Version);
        InitResources(_requester);
    }

    /// <summary>The base URL this client calls.</summary>
    public string BaseUrl { get; }

    /// <summary>
    /// Calls any path of the API directly — for an endpoint this SDK version has no method for yet.
    /// The answer comes back as it was sent (null when empty).
    /// </summary>
    /// <param name="method">GET, POST, …</param>
    /// <param name="path">The path, e.g. <c>/api/projects</c>.</param>
    /// <param name="query">A dictionary, an anonymous object or a query model; null for none.</param>
    /// <param name="body">Anything System.Text.Json can write; null for none.</param>
    /// <param name="options">Per-call timeout, retries and headers.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<JsonElement?> RawAsync(string method, string path, object? query = null, object? body = null, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _requester.SendAsync(method.ToUpperInvariant(), path, query, body, false, options, cancellationToken);

    /// <summary>Disposes the HttpClient the client made itself (not one passed in).</summary>
    public void Dispose() => _ownedHttp?.Dispose();

    private static string ReadVersion()
    {
        var v = typeof(CrafvyClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(v)) return "0.0.0";
        var plus = v!.IndexOf('+');
        return plus >= 0 ? v.Substring(0, plus) : v;
    }
}
