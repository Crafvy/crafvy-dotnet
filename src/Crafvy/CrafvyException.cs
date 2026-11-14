using System;
using System.Text.Json;

namespace Crafvy;

/// <summary>
/// Any error answer of the API — and a timeout (status 408) or a connection failure (status 0).
/// </summary>
/// <remarks>
/// Every refusal the API sends is <c>{"success": false, "error": "…", "code": "…"}</c>.
/// <see cref="Code"/> is the API's machine-readable reason (<c>tokenPermissionMissing</c>,
/// <c>crmSpaceNotFound</c>, …) — branch on it, not on the English message.
/// </remarks>
public class CrafvyException : Exception
{
    /// <summary>Creates the exception.</summary>
    public CrafvyException(int status, string? code, string message, JsonElement? body = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Status = status;
        Code = code;
        Body = body;
    }

    /// <summary>The HTTP status; 408 for a timeout, 0 when no answer came.</summary>
    public int Status { get; }

    /// <summary>The API's stable reason code, or null when it sent none.</summary>
    public string? Code { get; }

    /// <summary>The answer as it came (JSON, or a JSON string for a non-JSON answer), or null.</summary>
    public JsonElement? Body { get; }

    /// <inheritdoc />
    public override string ToString() => $"CrafvyException({Status}{(Code != null ? " " + Code : "")}): {Message}";

    internal static CrafvyException From(int status, JsonElement? body, TimeSpan? retryAfter)
    {
        var message = $"HTTP {status}";
        string? code = null;
        if (body is { ValueKind: JsonValueKind.Object } b)
        {
            if (b.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } text)
            {
                message = text;
            }
            if (b.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String)
            {
                code = c.GetString();
            }
        }
        return status switch
        {
            400 => new ValidationException(code, message, body),
            401 => new AuthenticationException(code, message, body),
            403 => new AuthorizationException(code, message, body),
            404 => new NotFoundException(code, message, body),
            409 => new ConflictException(code, message, body),
            429 => new RateLimitException(code, message, body, retryAfter),
            _ => new CrafvyException(status, code, message, body),
        };
    }
}

/// <summary>The request was invalid (400).</summary>
public class ValidationException : CrafvyException
{
    /// <summary>Creates the exception.</summary>
    public ValidationException(string? code, string message, JsonElement? body) : base(400, code, message, body) { }
}

/// <summary>No valid token (401).</summary>
public class AuthenticationException : CrafvyException
{
    /// <summary>Creates the exception.</summary>
    public AuthenticationException(string? code, string message, JsonElement? body) : base(401, code, message, body) { }
}

/// <summary>The token's owner may not do this, or the token lacks the permission (403).</summary>
public class AuthorizationException : CrafvyException
{
    /// <summary>Creates the exception.</summary>
    public AuthorizationException(string? code, string message, JsonElement? body) : base(403, code, message, body) { }
}

/// <summary>Not found (404).</summary>
public class NotFoundException : CrafvyException
{
    /// <summary>Creates the exception.</summary>
    public NotFoundException(string? code, string message, JsonElement? body) : base(404, code, message, body) { }
}

/// <summary>A conflict with the current state (409).</summary>
public class ConflictException : CrafvyException
{
    /// <summary>Creates the exception.</summary>
    public ConflictException(string? code, string message, JsonElement? body) : base(409, code, message, body) { }
}

/// <summary>Too many requests (429) — still, after the SDK's own retries.</summary>
public class RateLimitException : CrafvyException
{
    /// <summary>Creates the exception.</summary>
    public RateLimitException(string? code, string message, JsonElement? body, TimeSpan? retryAfter) : base(429, code, message, body)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>What the API said to wait (Retry-After), or null when it did not say.</summary>
    public TimeSpan? RetryAfter { get; }
}
