using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Crafvy.Tests;

/// <summary>For every operation: the request that must go out and the value that must come back.</summary>
public class ConformanceTests
{
    private static readonly JsonElement Suite = Conformance.Suite();
    private static readonly Dictionary<string, Calls.Call> Table = Calls.All();

    public static IEnumerable<object[]> Cases() =>
        Suite.GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("id").GetString()! });

    [Fact]
    public void TheSuiteHasCases() => Assert.True(Suite.GetProperty("cases").GetArrayLength() > 0);

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Operation(string id)
    {
        var c = Suite.GetProperty("cases").EnumerateArray().First(x => x.GetProperty("id").GetString() == id);
        Assert.True(Table.TryGetValue(id, out var call), $"no method for {id}");
        var rec = new Recorder();
        rec.Fallback = new Recorder.Reply { Body = c.GetProperty("response").GetRawText() };
        using var client = new CrafvyClient("pb_test", new CrafvyClientOptions { BaseUrl = "https://api.test", Retries = 0, HttpMessageHandler = rec });

        var args = c.GetProperty("pathArgs").EnumerateArray().Select(x => x.GetString()!).ToArray();
        JsonElement? body = c.GetProperty("body").ValueKind == JsonValueKind.Null ? null : c.GetProperty("body");
        JsonElement? query = c.GetProperty("query").ValueKind == JsonValueKind.Null ? null : c.GetProperty("query");
        var result = await call!(client, args, body, query);

        var expect = c.GetProperty("expect");
        var sent = rec.Last;
        Assert.Equal(expect.GetProperty("method").GetString(), sent.Request.Method.Method);
        Assert.Equal(expect.GetProperty("path").GetString(), sent.Request.RequestUri!.AbsolutePath);
        var got = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var raw = sent.Request.RequestUri.Query.TrimStart('?');
        if (raw.Length > 0)
        {
            foreach (var pair in raw.Split('&'))
            {
                var eq = pair.IndexOf('=');
                got[WebUtility.UrlDecode(pair.Substring(0, eq))] = Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
        }
        var want = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in expect.GetProperty("query").EnumerateObject()) want[p.Name] = p.Value.GetString()!;
        Assert.Equal(want, got);
        var wantBody = expect.GetProperty("body");
        if (wantBody.ValueKind != JsonValueKind.Null)
        {
            Assert.NotNull(sent.Body);
            var sentNode = System.Text.Json.Nodes.JsonNode.Parse(sent.Body!);
            Assert.True(Conformance.Same(Conformance.Normalise(sentNode), Conformance.Normalise(Conformance.Node(wantBody))), $"body {sent.BodyText}\nwant {wantBody.GetRawText()}");
        }
        Assert.Equal("Bearer pb_test", sent.Request.Headers.Authorization?.ToString());
        var gotResult = Conformance.Normalise(Conformance.Node(result));
        var wantResult = Conformance.Normalise(Conformance.Node(c.GetProperty("result")));
        Assert.True(Conformance.Same(gotResult, wantResult), $"result {gotResult?.ToJsonString()}\nwant   {c.GetProperty("result").GetRawText()}");
    }
}
