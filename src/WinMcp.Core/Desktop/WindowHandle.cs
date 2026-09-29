using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMcp.Core.Desktop;

/// <summary>A top-level or child window handle. Wire form: <c>hwnd:0x000A0B1C</c>.</summary>
[JsonConverter(typeof(WindowHandleJsonConverter))]
public readonly record struct WindowHandle(long Value)
{
    private const string Prefix = "hwnd:0x";

    public override string ToString() => $"{Prefix}{Value:X8}";

    public static bool TryParse(string? text, out WindowHandle handle)
    {
        handle = default;
        if (text is null || !text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!long.TryParse(text.AsSpan(Prefix.Length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) || value == 0)
            return false;
        handle = new WindowHandle(value);
        return true;
    }
}

public sealed class WindowHandleJsonConverter : JsonConverter<WindowHandle>
{
    public override WindowHandle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        WindowHandle.TryParse(reader.GetString(), out var handle)
            ? handle
            : throw new JsonException("Expected a window handle like 'hwnd:0x000A0B1C'.");

    public override void Write(Utf8JsonWriter writer, WindowHandle value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
