using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crafvy.Internal;

/// <summary>The SDK's JSON settings: absent (null) values are not written, unknown keys are ignored.</summary>
internal static class Json
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>An answer's body: JSON, or (a CSV export, plain text) a JSON string; null when empty.</summary>
    internal static JsonElement? Parse(byte[] raw)
    {
        if (raw.Length == 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(System.Text.Encoding.UTF8.GetString(raw), Options);
        }
    }

    /// <summary>How a value travels as text (a query value, a form field); null when absent.</summary>
    internal static string? Text(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => v.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => v.GetRawText(),
        _ => v.GetRawText(),
    };
}
