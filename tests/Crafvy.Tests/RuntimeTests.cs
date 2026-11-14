using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Crafvy.Internal;
using Xunit;

namespace Crafvy.Tests;

public class RuntimeTests
{
    private static CrafvyClient Client(Recorder rec, int retries = 3) =>
        new("pb_test", new CrafvyClientOptions { BaseUrl = "https://api.test/", Retries = retries, HttpMessageHandler = rec });

    private static Dictionary<string, string> Query(Recorder.Sent s)
    {
        var out_ = new Dictionary<string, string>();
        var raw = s.Request.RequestUri!.Query.TrimStart('?');
        if (raw.Length == 0) return out_;
        foreach (var pair in raw.Split('&'))
        {
            var eq = pair.IndexOf('=');
            out_[Uri.UnescapeDataString(pair.Substring(0, eq))] = Uri.UnescapeDataString(pair.Substring(eq + 1));
        }
        return out_;
    }

    [Fact]
    public async Task PathParametersAreEncodedAndHeadersSet()
    {
        var rec = new Recorder { Fallback = new() { Body = "{\"success\":true,\"data\":{\"id\":\"p1\",\"name\":\"Site\",\"extra\":{\"x\":1}}}" } };
        var p = await Client(rec).Projects.GetAsync("a b/c");
        Assert.StartsWith("/api/projects/a%20b%2Fc", rec.Last.Request.RequestUri!.AbsolutePath);
        Assert.Equal("crafvy-dotnet/" + CrafvyClient.Version, rec.Last.Request.Headers.UserAgent.ToString());
        // the package's own version, without the build's "+<commit>" suffix — not a
        // literal, or every release would fail its own tests
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", CrafvyClient.Version);
        Assert.Equal("Bearer pb_test", rec.Last.Request.Headers.Authorization!.ToString());
        Assert.Equal("p1", p!.Id);
        Assert.Equal("Site", p.Name);
        Assert.Equal(1, p.AdditionalProperties!["extra"].GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task ErrorsCarryStatusAndCode()
    {
        var rec = new Recorder { Fallback = new() { Status = 403, Body = "{\"success\":false,\"error\":\"No\",\"code\":\"tokenPermissionMissing\"}" } };
        var c = Client(rec, 0);
        var e = await Assert.ThrowsAsync<AuthorizationException>(() => c.RawAsync("GET", "/api/projects"));
        Assert.Equal(403, e.Status);
        Assert.Equal("tokenPermissionMissing", e.Code);
        Assert.Equal("No", e.Message);
        Assert.Equal("No", e.Body!.Value.GetProperty("error").GetString());

        rec.Fallback = new() { Status = 404, Body = "{\"success\":false,\"error\":\"Not found\"}" };
        var nf = await Assert.ThrowsAsync<NotFoundException>(() => c.Projects.GetAsync("x"));
        Assert.Null(nf.Code);

        rec.Fallback = new() { Status = 400, Body = "{\"error\":\"bad\"}" };
        await Assert.ThrowsAsync<ValidationException>(() => c.RawAsync("GET", "/api/x"));
        rec.Fallback = new() { Status = 401, Body = "{}" };
        await Assert.ThrowsAsync<AuthenticationException>(() => c.RawAsync("GET", "/api/x"));
        rec.Fallback = new() { Status = 409, Body = "{}" };
        await Assert.ThrowsAsync<ConflictException>(() => c.RawAsync("GET", "/api/x"));

        rec.Fallback = new() { Status = 418, Body = null };
        var other = await Assert.ThrowsAsync<CrafvyException>(() => c.RawAsync("GET", "/api/x"));
        Assert.Equal(418, other.Status);
        Assert.Equal("HTTP 418", other.Message);
    }

    [Fact]
    public async Task Retries429AndIdempotent5xxOnly()
    {
        var rec = new Recorder().Then(429, "", "0").Then(200, "{\"ok\":true}");
        var out_ = await Client(rec).RawAsync("POST", "/api/x", body: new Dictionary<string, object>());
        Assert.Equal(2, rec.Requests.Count);
        Assert.True(out_!.Value.GetProperty("ok").GetBoolean());

        rec = new Recorder().Then(503, "{\"error\":\"down\"}", "0");
        var e = await Assert.ThrowsAsync<CrafvyException>(() => Client(rec).RawAsync("POST", "/api/x", body: new Dictionary<string, object>()));
        Assert.Equal(503, e.Status);
        Assert.Single(rec.Requests); // a POST that failed with a 5xx is not sent again

        rec = new Recorder().Then(503, "", "0").Then(502, "", "0");
        rec.Fallback = new() { Body = "{\"ok\":1}" };
        await Client(rec).RawAsync("GET", "/api/x");
        Assert.Equal(3, rec.Requests.Count);

        rec = new Recorder { Fallback = new() { Status = 429, Body = "{\"error\":\"slow down\"}", RetryAfter = "0" } };
        var rl = await Assert.ThrowsAsync<RateLimitException>(() => Client(rec, 2).RawAsync("GET", "/api/x"));
        Assert.Equal(3, rec.Requests.Count);
        Assert.Equal(TimeSpan.Zero, rl.RetryAfter);
        Assert.Equal("slow down", rl.Message);

        rec = new Recorder().Then(429, "", "0");
        var opts = new RequestOptions { Retries = 0 };
        opts.Headers["X-Trace"] = "1";
        await Assert.ThrowsAsync<RateLimitException>(() => Client(rec).RawAsync("GET", "/api/x", options: opts));
        Assert.Single(rec.Requests);
        Assert.Equal("1", rec.Last.Request.Headers.GetValues("X-Trace").Single());
    }

    [Fact]
    public async Task StructuredQueryValuesTravelAsJson()
    {
        var rec = new Recorder();
        await Client(rec).RawAsync("GET", "/api/x", new Dictionary<string, object?>
        {
            ["filters"] = new[] { new Dictionary<string, object> { ["a"] = 1 } },
            ["on"] = true,
            ["skip"] = null,
            ["n"] = 2.5,
            ["whole"] = 3.0,
            ["text"] = "a b&c <ü>",
        });
        var q = Query(rec.Last);
        Assert.Equal("[{\"a\":1}]", q["filters"]);
        Assert.Equal("true", q["on"]);
        Assert.False(q.ContainsKey("skip"));
        Assert.Equal("2.5", q["n"]);
        Assert.Equal("3", q["whole"]);
        Assert.Equal("a b&c <ü>", q["text"]);
        Assert.Contains("text=a%20b%26c", rec.Last.Request.RequestUri!.Query);

        await Client(rec).Projects.ListAsync(new ProjectsListQuery());
        Assert.Equal("", rec.Last.Request.RequestUri!.Query);
    }

    [Fact]
    public async Task AbsentFieldsAreNotSentAndExplicitValuesAre()
    {
        var rec = new Recorder();
        await Client(rec).RawAsync("POST", "/api/x", body: new Dictionary<string, object> { ["done"] = false, ["tags"] = new string[0], ["html"] = "<a&b>" });
        Assert.Equal("{\"done\":false,\"tags\":[],\"html\":\"<a&b>\"}", rec.Last.BodyText);
        Assert.Equal("application/json", rec.Last.ContentType);

        await Client(rec).Crm.Records.CreateAsync("s", new CrmRecordsCreateBody());
        Assert.Equal("{}", rec.Last.BodyText);
    }

    [Fact]
    public async Task NonJsonAnswersComeBackAsText()
    {
        var rec = new Recorder { Fallback = new() { Body = "a,b\n1,2\n" } };
        var out_ = await Client(rec).RawAsync("GET", "/api/export.csv");
        Assert.Equal(JsonValueKind.String, out_!.Value.ValueKind);
        Assert.Equal("a,b\n1,2\n", out_.Value.GetString());
    }

    [Fact]
    public async Task MultipartUploadsFiles()
    {
        var rec = new Recorder { Fallback = new() { Body = "{\"success\":true}" } };
        await Client(rec).Assets.UploadAsync(new AssetsUploadBody
        {
            File = new CrafvyFile("a.txt", System.Text.Encoding.UTF8.GetBytes("hello"), "text/plain"),
            OrganizationId = "org",
            Visibility = AssetVisibility.Private,
        });
        Assert.StartsWith("multipart/form-data", rec.Last.ContentType);
        var body = rec.Last.BodyText;
        Assert.Contains("Content-Disposition: form-data; name=\"file\"; filename=\"a.txt\"", body);
        Assert.Contains("Content-Type: text/plain", body);
        Assert.Contains("\r\n\r\nhello\r\n", body);
        Assert.Contains("name=\"organizationId\"\r\n\r\norg\r\n", body);
        Assert.Contains("name=\"visibility\"\r\n\r\nprivate\r\n", body);
        Assert.DoesNotContain("workspaceId", body);
    }

    [Fact]
    public async Task TimeoutIs408AndCancellationIsTheCallers()
    {
        var slow = new SlowHandler();
        using var c = new CrafvyClient("t", new CrafvyClientOptions { BaseUrl = "https://api.test", Timeout = TimeSpan.FromMilliseconds(50), HttpMessageHandler = slow });
        var e = await Assert.ThrowsAsync<CrafvyException>(() => c.RawAsync("GET", "/api/x"));
        Assert.Equal(408, e.Status);

        using var cts = new CancellationTokenSource(50);
        using var c2 = new CrafvyClient("t", new CrafvyClientOptions { BaseUrl = "https://api.test", HttpMessageHandler = slow });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => c2.RawAsync("GET", "/api/x", cancellationToken: cts.Token));
    }

    [Fact]
    public void ModelsUseWireNamesAndConstants()
    {
        var json = JsonSerializer.Serialize(new AssetsUploadBody { OrganizationId = "o", Visibility = AssetVisibility.Public }, Json.Options);
        Assert.Equal("{\"organizationId\":\"o\",\"visibility\":\"public\"}", json);
        var back = JsonSerializer.Deserialize<AssetsUploadBody>("{\"organizationId\":\"o\",\"visibility\":\"someday-new\",\"unknown\":[1]}", Json.Options)!;
        Assert.Equal("someday-new", back.Visibility);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
