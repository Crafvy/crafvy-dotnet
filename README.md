# Crafvy — .NET SDK

The official .NET client of the [Crafvy](https://crafvy.com) API. Every
resource and method is generated from the API's description, the same one the
TypeScript, Python, PHP, Go and Java SDKs and the [reference](https://docs.crafvy.com/sdk)
come from — so a method here is exactly the one documented there.

.NET 8 and .NET Standard 2.0 (.NET Framework 4.6.2+). No dependency on .NET 8;
on .NET Standard, System.Text.Json (the package of what .NET 8 has built in).

```sh
dotnet add package Crafvy
```

## Use

```csharp
using Crafvy;

using var crafvy = new CrafvyClient(Environment.GetEnvironmentVariable("CRAFVY_TOKEN")); // null also reads CRAFVY_TOKEN
// new CrafvyClient(token, new CrafvyClientOptions { BaseUrl = "https://your-instance.example" }); // self-hosted

foreach (var project in await crafvy.Projects.ListAsync() ?? new())
{
    Console.WriteLine(project.Name);
}

var spaces = await crafvy.Crm.Spaces.ListAsync(new CrmSpacesListQuery { WorkspaceId = "…" });
```

- Resources are properties (`crafvy.Crm.Records`); methods are async and end
  in `Async`. Every method takes the path parameters, then the body, then the
  query (optional unless the API requires one), then `RequestOptions? options`
  and a `CancellationToken`.
- Models are classes with `{ get; set; }` properties in PascalCase, mapped to
  the API's names with `[JsonPropertyName]`. Every property is nullable: one
  left `null` is not sent; an explicit `false`, `0` or empty list is. A
  required property that may be null is sent as `null` when you leave it.
- Enums are `string`s, so a value the API adds later never breaks decoding;
  the named ones have a class of constants (`AssetVisibility.Private`).
- A body or property that is one of several shapes, or `any`, is an
  `object?`: pass the model you mean (or a dictionary); answers of that kind
  come as a `JsonElement`. Models that allow more keys keep them in
  `AdditionalProperties`.
- `crafvy.RawAsync("GET", "/api/...", query, body)` calls any endpoint and
  returns the answer as a `JsonElement?`.

## Tokens

A token acts as its owner: their roles, workspaces, feature gates and plan
limits apply exactly as in the dashboard. Create one under **Settings →
Integrations → API Access Tokens**. A token made with permissions only reaches
those families of routes; each method's documentation names the permission
it needs.

## Errors

```csharp
try
{
    await crafvy.Projects.GetAsync("missing");
}
catch (NotFoundException)
{
    // …
}
catch (CrafvyException e)
{
    Console.WriteLine($"{e.Status} {e.Code} {e.Message}");
}
```

`Code` is the API's stable reason (`tokenPermissionMissing`, …) — branch on
it, not on the English text. Subclasses: `ValidationException` (400),
`AuthenticationException` (401), `AuthorizationException` (403),
`NotFoundException` (404), `ConflictException` (409), `RateLimitException`
(429, `RetryAfter` when the API said). 429 answers are retried with back-off
(respecting `Retry-After`); a 5xx is retried only for GET, HEAD, PUT and
DELETE, never for a POST that may have been carried out. A timeout is a
`CrafvyException` with status 408; cancelling your token throws
`OperationCanceledException` as usual.

## Options

```csharp
var crafvy = new CrafvyClient(token, new CrafvyClientOptions
{
    Retries = 5,
    Timeout = TimeSpan.FromSeconds(30),
    HttpClient = httpClientFactory.CreateClient("crafvy"), // or HttpMessageHandler = …
});

await crafvy.Projects.ListAsync(options: new RequestOptions
{
    Timeout = TimeSpan.FromSeconds(5),
    Headers = { ["X-Trace"] = "1" },
});
```

Tests can pass an `HttpMessageHandler` that records requests and answers
them, instead of real HTTP.

## Uploads

```csharp
var asset = await crafvy.Assets.UploadAsync(new AssetsUploadBody
{
    File = CrafvyFile.FromPath("logo.png", "image/png"),
    OrganizationId = org,
});
```

## Releases

Published to NuGet as `Crafvy`. Changelog: [CHANGELOG.md](CHANGELOG.md).
Generated — changes go into the API's description in the Crafvy repository,
not into this package.
