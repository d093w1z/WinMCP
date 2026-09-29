using WinMcp.Core.Automation;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>Plan §E.3 test 8 plus combo options, against the live TestApp.</summary>
[Trait("Category", "Windows")]
public sealed class InspectElementTests : IClassFixture<TestAppSession>, IDisposable
{
    private readonly TestAppSession _app;
    private readonly WinMcpServices _services = new();

    public InspectElementTests(TestAppSession app)
    {
        _app = app;
        _app.Reset();
    }

    public void Dispose() => _services.Dispose();

    private Task<ElementDetail> Inspect(string automationId) =>
        _services.Tree.InspectAsync(_app.WindowHandle.ToString(), null, new ElementLocator(AutomationId: automationId), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Edit_control_details_correlate_uia_and_win32()
    {
        var detail = await Inspect("nameTextBox");

        Assert.Equal("Edit", detail.ControlType);
        Assert.Equal("Name:", detail.Name);
        Assert.Contains("EDIT", detail.ClassName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("WinForm", detail.FrameworkId);
        Assert.NotNull(detail.Hwnd);
        Assert.Equal(detail.Hwnd, detail.HostHwnd);
        Assert.Equal(_app.Find("nameTextBox").Properties.NativeWindowHandle.Value, (nint)detail.Hwnd!.Value.Value);
        Assert.NotNull(detail.ControlId);
        Assert.Contains("Value", detail.Patterns);
        Assert.True(detail.Focusable);
        Assert.True(detail.Enabled);
        Assert.True(detail.Bounds.Width > 0);
        Assert.Equal(new SuggestedLocator("nameTextBox", null, null, null, Unique: true), detail.Locator);
    }

    [Fact]
    public async Task Combo_options_are_read_without_opening_the_dropdown()
    {
        var detail = await Inspect("typeComboBox");

        Assert.Equal(["Text", "HTML", "Markdown"], detail.Options);
        Assert.Equal(3, detail.OptionCount);
        Assert.Contains("collapsed", detail.States!);
        // No DropDown/DropDownClosed events: the app never saw its combo open.
        Assert.Equal("Events: 0 []", _app.Text("eventLogLabel"));
    }

    [Fact]
    public async Task Disabled_button_reports_state_and_patterns()
    {
        var detail = await Inspect("advancedButton");

        Assert.False(detail.Enabled);
        Assert.Contains("disabled", detail.States!);
        Assert.Contains("Invoke", detail.Patterns); // M0: UIA still advertises Invoke on disabled buttons
    }

    [Fact]
    public async Task Non_combo_has_no_options() =>
        Assert.Null((await Inspect("applyButton")).Options);
}
