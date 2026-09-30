using WinMcp.Core.Automation;
using WinMcp.Core.Errors;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>Plan §E.3 test 20 and send_keys behaviour, against the live TestApp with charmap as the "other" application.</summary>
[Trait("Category", "Windows")]
public sealed class SendKeysTests : IClassFixture<TestAppSession>, IDisposable
{
    private readonly TestAppSession _app;
    private readonly WinMcpServices _services = WinMcpServices.Control();

    public SendKeysTests(TestAppSession app)
    {
        _app = app;
        _app.Reset();
    }

    public void Dispose() => _services.Dispose();

    private Task<ActionResult> Send(string? text = null, string? keys = null, string? automationId = null) =>
        _services.Interaction.SendKeysAsync(
            _app.WindowHandle.ToString(), null,
            new ElementLocator(AutomationId: automationId), KeyInputParser.Parse(text, keys), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Types_text_into_a_focused_element_through_the_apps_own_handlers()
    {
        var result = await Send(text: "Mukesh", automationId: "nameTextBox");

        Assert.Equal("win32.SendInput", result.Method);
        TestAppSession.WaitUntil(() => _app.Value("nameTextBox") == "Mukesh", "the name is typed");
        Assert.Contains("nameTextBox.TextChanged", _app.Text("eventLogLabel"));
    }

    [Fact]
    public async Task Key_chords_edit_the_focused_control()
    {
        await Send(text: "Mukesh", automationId: "nameTextBox");
        TestAppSession.WaitUntil(() => _app.Value("nameTextBox") == "Mukesh", "the name is typed");

        await Send(keys: "Home, Shift+End, Backspace", automationId: "nameTextBox");

        TestAppSession.WaitUntil(() => _app.Value("nameTextBox") == "", "the name is cleared");
    }

    [Fact]
    public async Task Never_types_into_the_application_that_had_the_foreground()
    {
        using var other = new CharmapSession();
        var charmapEdit = other.Window.FindFirstDescendant(cf => cf.ByAutomationId("104"))!; // "Characters to copy"
        var before = charmapEdit.Patterns.Value.Pattern.Value.Value;
        charmapEdit.Focus();
        TestAppSession.WaitUntil(() => charmapEdit.Properties.HasKeyboardFocus.Value, "charmap has focus");

        try
        {
            await Send(text: "Mukesh", automationId: "nameTextBox");
            // If Windows allowed the foreground switch, the text must have landed in the TestApp...
            TestAppSession.WaitUntil(() => _app.Value("nameTextBox") == "Mukesh", "the name is typed into the TestApp");
            TestContext.Current.TestOutputHelper?.WriteLine("outcome: foreground switched to the TestApp and the text was typed there");
        }
        catch (WinMcpException ex) when (ex.Error.Code == WinMcpErrorCode.FocusFailed)
        {
            // ...otherwise WinMCP must have refused rather than typed. Both outcomes are acceptable; typing elsewhere isn't.
            TestContext.Current.TestOutputHelper?.WriteLine($"outcome: refused with FOCUS_FAILED ({ex.Error.Message})");
        }

        Assert.Equal(before, charmapEdit.Patterns.Value.Pattern.Value.Value);
    }

    [Fact]
    public async Task Password_fields_and_blocked_chords_never_reach_the_keyboard()
    {
        Assert.Equal(WinMcpErrorCode.OperationNotPermitted,
            Assert.Throws<WinMcpException>(() => KeyInputParser.Parse(null, "Alt+Tab")).Error.Code);

        // The TestApp has no password box; the refusal path is covered by unit tests. Here: a disabled target is refused.
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Send(text: "x", automationId: "advancedButton"));
        Assert.Equal(WinMcpErrorCode.ElementDisabled, ex.Error.Code);
    }
}
