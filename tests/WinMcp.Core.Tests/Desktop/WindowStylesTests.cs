using WinMcp.Core.Desktop;

namespace WinMcp.Core.Tests.Desktop;

public sealed class WindowStylesTests
{
    [Fact]
    public void Overlapped_window_with_caption_is_reported_as_WS_CAPTION()
    {
        // WS_OVERLAPPEDWINDOW | WS_VISIBLE | WS_CLIPSIBLINGS
        var names = WindowStyles.DecodeStyle(0x14CF0000);

        Assert.Equal(
            ["WS_CAPTION", "WS_VISIBLE", "WS_THICKFRAME", "WS_SYSMENU", "WS_MINIMIZEBOX", "WS_MAXIMIZEBOX", "WS_CLIPSIBLINGS"],
            names);
    }

    [Fact]
    public void Border_without_dialog_frame_is_not_a_caption() =>
        Assert.Equal(["WS_POPUP", "WS_BORDER"], WindowStyles.DecodeStyle(0x80800000));

    [Fact]
    public void Unknown_bits_are_reported_not_dropped() =>
        Assert.Equal(["WS_EX_TOPMOST", "0x00000002"], WindowStyles.DecodeExStyle(0x0000000A));

    [Fact]
    public void Zero_decodes_to_nothing()
    {
        Assert.Empty(WindowStyles.DecodeStyle(0));
        Assert.Empty(WindowStyles.DecodeExStyle(0));
    }
}
