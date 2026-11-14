using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crafvy;

/// <summary>
/// An upload for a multipart operation (<c>Assets.UploadAsync</c>, …):
/// <code>await crafvy.Assets.UploadAsync(new AssetsUploadBody { File = CrafvyFile.FromPath("logo.png"), OrganizationId = org });</code>
/// </summary>
[JsonConverter(typeof(CrafvyFileConverter))]
public sealed class CrafvyFile
{
    /// <summary>An upload of these bytes.</summary>
    /// <param name="name">The file name the API sees; null: the form field's name.</param>
    /// <param name="data">The bytes.</param>
    /// <param name="contentType">The media type; null: application/octet-stream.</param>
    public CrafvyFile(string? name, byte[] data, string? contentType = null)
    {
        Name = name;
        Data = data ?? throw new ArgumentNullException(nameof(data));
        ContentType = contentType;
    }

    /// <summary>The file name the API sees.</summary>
    public string? Name { get; }

    /// <summary>The media type.</summary>
    public string? ContentType { get; }

    /// <summary>The bytes.</summary>
    public byte[] Data { get; }

    /// <summary>Reads a file from disk.</summary>
    public static CrafvyFile FromPath(string path, string? contentType = null) =>
        new(Path.GetFileName(path), File.ReadAllBytes(path), contentType);

    /// <summary>Reads a stream to its end.</summary>
    public static CrafvyFile FromStream(Stream stream, string name, string? contentType = null)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return new CrafvyFile(name, copy.ToArray(), contentType);
    }

    /// <inheritdoc />
    public override string ToString() => $"CrafvyFile({Name}, {ContentType}, {Data.Length} bytes)";
}

/// <summary>A file in JSON (a model's dump): its name, type and size — never the bytes.</summary>
internal sealed class CrafvyFileConverter : JsonConverter<CrafvyFile>
{
    public override CrafvyFile? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, CrafvyFile value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        writer.WriteString("contentType", value.ContentType);
        writer.WriteNumber("size", value.Data.Length);
        writer.WriteEndObject();
    }
}
