using System.Text.Json;
using WinMcp.Core.Desktop;

namespace WinMcp.Core.Tests.Desktop;

public sealed class WindowHandleTests
{
    [Fact]
    public void Formats_as_prefixed_eight_digit_hex() =>
        Assert.Equal("hwnd:0x000A0B1C", new WindowHandle(0xA0B1C).ToString());

    [Theory]
    [InlineData("hwnd:0x000A0B1C", 0xA0B1C)]
    [InlineData("HWND:0xa0b1c", 0xA0B1C)]
    [InlineData("hwnd:0x1234567890", 0x1234567890)]
    public void Parses_valid_handles(string text, long expected)
    {
        Assert.True(WindowHandle.TryParse(text, out var handle));
        Assert.Equal(expected, handle.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0x000A0B1C")]
    [InlineData("hwnd:0x")]
    [InlineData("hwnd:0xZZ")]
    [InlineData("hwnd:0x0")]
    public void Rejects_invalid_handles(string? text) =>
        Assert.False(WindowHandle.TryParse(text, out _));

    [Fact]
    public void Round_trips_through_json()
    {
        var json = JsonSerializer.Serialize(new WindowHandle(0xA0B1C));

        Assert.Equal("\"hwnd:0x000A0B1C\"", json);
        Assert.Equal(new WindowHandle(0xA0B1C), JsonSerializer.Deserialize<WindowHandle>(json));
    }
}
