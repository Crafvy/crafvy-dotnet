using System;
using System.Collections.Generic;

namespace Crafvy;

/// <summary>
/// Overrides for one call — the <c>options</c> argument of every method:
/// <code>await crafvy.Projects.ListAsync(options: new RequestOptions { Timeout = TimeSpan.FromSeconds(5) });</code>
/// </summary>
public sealed class RequestOptions
{
    /// <summary>How long this call may take, retries included; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> means no limit. Null: the client's.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>How often a 429 (or a 5xx of GET, HEAD, PUT, DELETE) is retried. Null: the client's.</summary>
    public int? Retries { get; set; }

    /// <summary>Headers to send with this call.</summary>
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
}
