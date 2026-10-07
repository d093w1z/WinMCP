using WinMcp.Core.Automation;
using WinMcp.Core.Errors;

namespace WinMcp.Core.Tests.Automation;

public sealed class KeyInputParserTests
{
    [Fact]
    public void Text_is_passed_through_literally() =>
        Assert.Equal(new KeyInput.Text("Ctrl+A is text here"), KeyInputParser.Parse("Ctrl+A is text here", null));

    [Fact]
    public void Keys_parse_into_chords_with_virtual_keys()
    {
        var chords = Assert.IsType<KeyInput.Chords>(KeyInputParser.Parse(null, "ctrl+a, Backspace , Ctrl+Shift+s, F5, pgdn")).Sequence;

        Assert.Equal(["Ctrl+A", "Backspace", "Ctrl+Shift+S", "F5", "PageDown"], chords.Select(c => c.ToString()));
        Assert.Equal((ushort)'A', chords[0].VirtualKey);
        Assert.Equal((ushort)0x74, chords[3].VirtualKey); // VK_F5
    }

    [Theory]
    [InlineData("Win+R")]
    [InlineData("Ctrl+Win+D")]
    [InlineData("Alt+Tab")]
    [InlineData("alt+shift+tab")]
    [InlineData("Ctrl+Esc")]
    [InlineData("Ctrl+Shift+Escape")]
    [InlineData("Ctrl+Alt+Del")]
    public void Focus_stealing_and_system_chords_are_refused_by_policy(string keys) =>
        Assert.Equal(WinMcpErrorCode.OperationNotPermitted,
            Assert.Throws<WinMcpException>(() => KeyInputParser.Parse(null, keys)).Error.Code);

    [Theory]
    [InlineData(null, null)]
    [InlineData("x", "Enter")]
    [InlineData("", null)]
    [InlineData(null, "Ctrl+")]
    [InlineData(null, "Ctrl+A+B")]
    [InlineData(null, "Hyper+X")]
    [InlineData(null, " , ")]
    public void Invalid_input_is_rejected(string? text, string? keys) =>
        Assert.Equal(WinMcpErrorCode.InvalidArgument,
            Assert.Throws<WinMcpException>(() => KeyInputParser.Parse(text, keys)).Error.Code);

    [Fact]
    public void Limits_are_enforced()
    {
        Assert.Throws<WinMcpException>(() => KeyInputParser.Parse(new string('x', KeyInputParser.MaxTextLength + 1), null));
        Assert.Throws<WinMcpException>(() => KeyInputParser.Parse(null, string.Join(",", Enumerable.Repeat("Tab", KeyInputParser.MaxChords + 1))));
    }
}
