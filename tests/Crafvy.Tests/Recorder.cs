using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Crafvy.Tests;

/// <summary>A handler that keeps what was sent and answers from a script.</summary>
internal sealed class Recorder : HttpMessageHandler
{
    internal sealed class Sent
    {
        public HttpRequestMessage Request = null!;
        public byte[]? Body;
        public string? ContentType;
        public string BodyText => Body == null ? "" : Encoding.UTF8.GetString(Body);
    }

    internal sealed class Reply
    {
        public int Status = 200;
        public string? Body;
        public string? RetryAfter;
    }

    public readonly List<Sent> Requests = new();
    public readonly Queue<Reply> Script = new();
    public Reply Fallback = new() { Body = "{}" };

    public Recorder Then(int status, string? body, string? retryAfter = null)
    {
        Script.Enqueue(new Reply { Status = status, Body = body, RetryAfter = retryAfter });
        return this;
    }

    public Sent Last => Requests[Requests.Count - 1];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sent = new Sent { Request = request };
        if (request.Content != null)
        {
            sent.Body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            sent.ContentType = request.Content.Headers.ContentType?.ToString();
        }
        Requests.Add(sent);
        var reply = Script.Count > 0 ? Script.Dequeue() : Fallback;
        var res = new HttpResponseMessage((HttpStatusCode)reply.Status)
        {
            Content = new ByteArrayContent(reply.Body == null ? new byte[0] : Encoding.UTF8.GetBytes(reply.Body)),
            RequestMessage = request,
        };
        if (reply.RetryAfter != null) res.Headers.TryAddWithoutValidation("Retry-After", reply.RetryAfter);
        return res;
    }
}
