using WinMcp.Core.Desktop;

namespace WinMcp.Core.Tests.Desktop;

public sealed class FrameworkHintTests
{
    [Theory]
    [InlineData("WindowsForms10.Window.8.app.0.2bf8098_r6_ad1", "winforms")]
    [InlineData("HwndWrapper[MyApp;;7d2e]", "wpf")]
    [InlineData("AfxFrameOrView140u", "mfc")]
    [InlineData("Afx:00400000:8:00010003:00000000:00000000", "mfc")]
    [InlineData("WinUIDesktopWin32WindowClass", "winui3")]
    [InlineData("ApplicationFrameWindow", "uwp")]
    [InlineData("Chrome_WidgetWin_1", "chromium")]
    [InlineData("Qt6110QWindowIcon", "qt")]
    [InlineData("SunAwtFrame", "java-awt")]
    [InlineData("#32770", "win32-dialog")]
    [InlineData("Notepad", null)]
    public void Recognizes_framework_from_top_level_class(string className, string? expected) =>
        Assert.Equal(expected, FrameworkHint.Guess(className, []));

    [Fact]
    public void Falls_back_to_child_classes() =>
        Assert.Equal("mfc", FrameworkHint.Guess("MyCustomFrame", ["Button", "AfxWnd140u", "Edit"]));

    [Fact]
    public void Dialog_hosting_framework_children_reports_the_framework() =>
        Assert.Equal("mfc", FrameworkHint.Guess("#32770", ["Static", "AfxWnd140u"]));

    [Fact]
    public void Plain_dialog_with_standard_controls_is_a_win32_dialog() =>
        Assert.Equal("win32-dialog", FrameworkHint.Guess("#32770", ["Static", "Button", "Edit"]));
}
