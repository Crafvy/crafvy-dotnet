using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crafvy.Internal;

namespace Crafvy.Tests;

internal static class Conformance
{
    /// <summary>A case's argument as the type the method takes.</summary>
    internal static T? Convert<T>(JsonElement? e) =>
        e is not { } v || v.ValueKind == JsonValueKind.Null ? default : v.Deserialize<T>(Json.Options);

    /// <summary>
    /// The shared suite: conformance.json at the root of the public repository, in sdks/
    /// of the Crafvy repository — found by walking up from the test's folder.
    /// </summary>
    internal static JsonElement Suite()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, "conformance.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                return doc.RootElement.Clone();
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("conformance.json not found");
    }

    /// <summary>
    /// Drops object keys whose value is null: a typed model sends null for a required,
    /// nullable field the sample left out (a recursive shape cut short), which says the same.
    /// </summary>
    internal static JsonNode? Normalise(JsonNode? n)
    {
        switch (n)
        {
            case JsonObject o:
                foreach (var key in o.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList()) o.Remove(key);
                foreach (var kv in o.ToList()) Normalise(kv.Value);
                break;
            case JsonArray a:
                foreach (var x in a) Normalise(x);
                break;
        }
        return n;
    }

    /// <summary>JSON equality where 2 and 2.0 are the same number.</summary>
    internal static bool Same(JsonNode? a, JsonNode? b)
    {
        if (a == null || b == null) return a == null && b == null;
        if (a is JsonObject oa && b is JsonObject ob)
        {
            if (oa.Count != ob.Count) return false;
            foreach (var kv in oa)
            {
                if (!ob.TryGetPropertyValue(kv.Key, out var other) || !Same(kv.Value, other)) return false;
            }
            return true;
        }
        if (a is JsonArray aa && b is JsonArray ab)
        {
            if (aa.Count != ab.Count) return false;
            for (var i = 0; i < aa.Count; i++) if (!Same(aa[i], ab[i])) return false;
            return true;
        }
        if (a is JsonValue va && b is JsonValue vb)
        {
            var ea = JsonSerializer.SerializeToElement(va);
            var eb = JsonSerializer.SerializeToElement(vb);
            if (ea.ValueKind == JsonValueKind.Number && eb.ValueKind == JsonValueKind.Number) return ea.GetDecimal() == eb.GetDecimal();
            return ea.ValueKind == eb.ValueKind && ea.GetRawText() == eb.GetRawText();
        }
        return false;
    }

    internal static JsonNode? Node(object? value) =>
        value == null ? null : JsonSerializer.SerializeToNode(value, value.GetType(), Json.Options);

    internal static JsonNode? Node(JsonElement e) => JsonNode.Parse(e.GetRawText());
}
