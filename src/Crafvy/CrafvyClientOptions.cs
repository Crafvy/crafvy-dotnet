using System;
using System.Net.Http;

namespace Crafvy;

/// <summary>Settings of a <see cref="CrafvyClient"/>.</summary>
public sealed class CrafvyClientOptions
{
    /// <summary>Another instance (self-hosted, staging). Default: CRAFVY_BASE_URL, then <see cref="CrafvyClient.DefaultBaseUrl"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>How long one call may take, retries included. Default 60 s; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> means no limit.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How often a 429 (or a 5xx of GET, HEAD, PUT, DELETE) is retried. Default 3.</summary>
    public int Retries { get; set; } = 3;

    /// <summary>The handler requests go through — a proxy, a custom TLS setup, or a test double that records and answers.</summary>
    public HttpMessageHandler? HttpMessageHandler { get; set; }

    /// <summary>An HttpClient to send with (from IHttpClientFactory, say). The client does not dispose it. Wins over <see cref="HttpMessageHandler"/>.</summary>
    public HttpClient? HttpClient { get; set; }
}
