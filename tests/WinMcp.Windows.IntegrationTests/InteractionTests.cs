using System.Text.Json;
using WinMcp.Core.Automation;
using WinMcp.Core.Errors;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>Plan §E.3 tests 9–17 through the production Core + Windows services, against the live TestApp.</summary>
[Trait("Category", "Windows")]
public sealed class InteractionTests : IClassFixture<TestAppSession>, IDisposable
{
    private const string GoldenStatus = "Status: Applied: Name=Mukesh; Type=HTML; Feature=On";
    private readonly TestAppSession _app;
    private readonly WinMcpServices _services = WinMcpServices.Control();

    public InteractionTests(TestAppSession app)
    {
        _app = app;
        _app.Reset();
    }

    public void Dispose() => _services.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Hwnd => _app.WindowHandle.ToString();

    private Task<ActionResult> Act(string automationId, ElementAction action) =>
        _services.Interaction.PerformAsync(Hwnd, null, new ElementLocator(AutomationId: automationId), action, Token);

    private Task<WaitResult> WaitText(string automationId, WaitCondition condition, string text, int timeoutMs = 5000) =>
        _services.Tree.WaitAsync(Hwnd, null, new ElementLocator(AutomationId: automationId), condition, text, timeoutMs, Token);

    [Fact]
    public async Task Golden_scenario_through_WinMcp_services()
    {
        var name = await Act("nameTextBox", new ElementAction.SetValue("Mukesh"));
        var type = await Act("typeComboBox", new ElementAction.Select("HTML"));
        var feature = await Act("enableCheckBox", new ElementAction.SetToggle(true));
        var apply = await Act("applyButton", new ElementAction.Invoke());
        var status = await WaitText("statusLabel", WaitCondition.TextEquals, GoldenStatus);

        Assert.Equal(GoldenStatus, status.Element!.Name);
        Assert.Equal(("uia.ValuePattern", "Mukesh"), (name.Method, name.ValueAfter));
        Assert.Equal(("uia.SelectionItemPattern", "HTML"), (type.Method, type.ValueAfter));
        Assert.Equal(("none", false), (feature.Method, feature.Changed)); // already on
        Assert.Equal("win32.BM_CLICK", apply.Method); // HWND push buttons are clicked by message, not InvokePattern (M9)
        Assert.Contains("nameTextBox.TextChanged", _app.Text("eventLogLabel"));
        Assert.Contains("typeComboBox.SelectedIndexChanged", _app.Text("eventLogLabel"));
    }

    [Fact]
    public async Task Select_option_restores_the_collapsed_combo_and_is_idempotent()
    {
        await Act("typeComboBox", new ElementAction.Select("HTML"));
        var again = await Act("typeComboBox", new ElementAction.Select("HTML"));

        Assert.False(again.Changed);
        Assert.Equal("HTML", _app.Value("typeComboBox"));
        Assert.Equal(FlaUI.Core.Definitions.ExpandCollapseState.Collapsed,
            _app.Find("typeComboBox").Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value);
    }

    [Fact]
    public async Task Unknown_option_lists_the_available_ones_and_leaves_the_combo_as_it_was()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Act("typeComboBox", new ElementAction.Select("PDF")));

        Assert.Equal(WinMcpErrorCode.OptionNotFound, ex.Error.Code);
        Assert.Equal(["Text", "HTML", "Markdown"], JsonSerializer.SerializeToElement(ex.Error.Details!["available"]).EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("Text", _app.Value("typeComboBox"));
        Assert.Equal(FlaUI.Core.Definitions.ExpandCollapseState.Collapsed,
            _app.Find("typeComboBox").Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value);
    }

    [Fact]
    public async Task Set_toggle_sets_a_state_and_repeating_it_is_harmless()
    {
        var first = await Act("enableCheckBox", new ElementAction.SetToggle(false));
        var second = await Act("enableCheckBox", new ElementAction.SetToggle(false));

        Assert.Equal((true, "off"), (first.Changed, first.StateAfter));
        Assert.Equal((false, "off"), (second.Changed, second.StateAfter));
        Assert.Equal(FlaUI.Core.Definitions.ToggleState.Off, _app.ToggleState("enableCheckBox"));
    }

    [Fact]
    public async Task Disabled_button_is_refused_and_its_handler_never_runs()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Act("advancedButton", new ElementAction.Invoke()));

        Assert.Equal(WinMcpErrorCode.ElementDisabled, ex.Error.Code);
        await Task.Delay(300, Token); // give a (wrongly) delivered click time to show up
        Assert.Equal("Status: Ready", _app.Text("statusLabel")); // M0: raw UIA Invoke would have run it
    }

    [Fact]
    public async Task Enabling_a_control_through_the_ui_then_invoking_it_works()
    {
        await Act("nameTextBox", new ElementAction.SetValue("Mukesh"));
        await _services.Tree.WaitAsync(Hwnd, null, new ElementLocator(AutomationId: "advancedButton"), WaitCondition.Enabled, null, 3000, Token);

        await Act("advancedButton", new ElementAction.Invoke());

        await WaitText("statusLabel", WaitCondition.TextEquals, "Status: Advanced options opened", 3000);
    }

    [Fact]
    public async Task Wait_for_follows_a_slow_operation_and_times_out_cleanly()
    {
        await Act("nameTextBox", new ElementAction.SetValue("Mukesh"));
        await Act("slowApplyButton", new ElementAction.Invoke());

        var tooShort = await Assert.ThrowsAsync<WinMcpException>(() => WaitText("statusLabel", WaitCondition.TextContains, "Applied", timeoutMs: 200));
        Assert.Equal(WinMcpErrorCode.Timeout, tooShort.Error.Code);
        Assert.Equal("Status: Applying...", tooShort.Error.Details!["last_text"]);

        var done = await WaitText("statusLabel", WaitCondition.TextContains, "Applied", timeoutMs: 5000);
        Assert.Equal("Status: Applied: Name=Mukesh; Type=Text; Feature=On", done.Element!.Name);
    }

    [Fact]
    public async Task Dynamic_field_can_be_waited_for_and_filled()
    {
        await Act("addFieldButton", new ElementAction.Invoke());
        await _services.Tree.WaitAsync(Hwnd, null, new ElementLocator(AutomationId: "dynamicTextBox1"), WaitCondition.Exists, null, 3000, Token);

        var result = await Act("dynamicTextBox1", new ElementAction.SetValue("hello"));

        Assert.Equal("hello", result.ValueAfter);
        Assert.Contains("dynamicTextBox1.TextChanged", _app.Text("eventLogLabel"));
    }

    [Fact]
    public async Task Hidden_control_cannot_be_targeted() =>
        Assert.Equal(WinMcpErrorCode.ElementNotFound,
            (await Assert.ThrowsAsync<WinMcpException>(() => Act("hiddenTextBox", new ElementAction.SetValue("x")))).Error.Code);

    [Fact]
    public async Task Every_action_is_audited_with_its_method()
    {
        // Refused first: setting a name would enable the Advanced button.
        await Assert.ThrowsAsync<WinMcpException>(() => Act("advancedButton", new ElementAction.Invoke()));
        await Act("nameTextBox", new ElementAction.SetValue("Mukesh"));

        var lines = Directory.GetFiles(_services.AuditDirectory, "audit-*.jsonl").SelectMany(File.ReadAllLines).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Contains("\"outcome\":\"ELEMENT_DISABLED\"", lines[0]);
        Assert.Contains("\"method\":\"uia.ValuePattern\"", lines[1]);
    }
}
