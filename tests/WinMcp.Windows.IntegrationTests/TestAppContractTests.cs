using FlaUI.Core.Definitions;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>Pins the TestApp v1 contract (plan §E.1) that every later WinMCP integration and E2E test relies on.</summary>
[Trait("Category", "Windows")]
public sealed class TestAppContractTests : IDisposable
{
    private const string GoldenStatus = "Status: Applied: Name=Mukesh; Type=HTML; Feature=On";
    private readonly TestAppSession _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Starts_in_the_documented_initial_state()
    {
        Assert.Equal("WinMCP Test App", _app.Window.Title);
        Assert.Equal("Status: Ready", _app.Text("statusLabel"));
        Assert.Equal("Events: 0 []", _app.Text("eventLogLabel"));
        Assert.Equal("", _app.Value("nameTextBox"));
        Assert.Equal("Text", _app.Value("typeComboBox"));
        Assert.Equal(ToggleState.On, _app.ToggleState("enableCheckBox"));
        Assert.False(_app.Find("advancedButton").Properties.IsEnabled.Value);
        Assert.Null(_app.TryFind("hiddenTextBox"));
    }

    [Fact]
    public void Golden_scenario_produces_the_exact_status_through_real_event_handlers()
    {
        _app.SetValue("nameTextBox", "Mukesh");
        _app.SelectOption("typeComboBox", "HTML");
        _app.Invoke("applyButton");

        TestAppSession.WaitUntil(() => _app.Text("statusLabel") == GoldenStatus, "status shows the applied values");
        var log = _app.Text("eventLogLabel");
        Assert.Contains("nameTextBox.TextChanged", log);
        Assert.Contains("typeComboBox.SelectedIndexChanged", log);
        Assert.Equal(ExpandCollapseState.Collapsed, _app.Find("typeComboBox").Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value);
    }

    [Fact]
    public void Advanced_is_enabled_only_when_feature_is_on_and_name_is_set()
    {
        _app.SetValue("nameTextBox", "Mukesh");
        TestAppSession.WaitUntil(() => _app.Find("advancedButton").Properties.IsEnabled.Value, "Advanced is enabled");

        _app.Toggle("enableCheckBox");
        TestAppSession.WaitUntil(() => !_app.Find("advancedButton").Properties.IsEnabled.Value, "Advanced is disabled again");
    }

    [Fact]
    public void Cancel_restores_defaults_removes_dynamic_fields_and_clears_the_event_log()
    {
        _app.SetValue("nameTextBox", "Mukesh");
        _app.SelectOption("typeComboBox", "Markdown");
        _app.Toggle("enableCheckBox");
        _app.Invoke("addFieldButton");
        TestAppSession.WaitUntil(() => _app.TryFind("dynamicTextBox1") is not null, "dynamic field exists");

        _app.Invoke("cancelButton");

        TestAppSession.WaitUntil(() => _app.Text("eventLogLabel") == "Events: 0 []", "event log is cleared");
        Assert.Equal("Status: Ready", _app.Text("statusLabel"));
        Assert.Equal("", _app.Value("nameTextBox"));
        Assert.Equal("Text", _app.Value("typeComboBox"));
        Assert.Equal(ToggleState.On, _app.ToggleState("enableCheckBox"));
        Assert.Null(_app.TryFind("dynamicTextBox1"));
    }

    [Fact]
    public void Slow_apply_reports_applying_then_applied()
    {
        _app.SetValue("nameTextBox", "Mukesh");
        _app.Invoke("slowApplyButton");

        TestAppSession.WaitUntil(() => _app.Text("statusLabel") == "Status: Applying...", "status shows Applying...");
        TestAppSession.WaitUntil(
            () => _app.Text("statusLabel") == "Status: Applied: Name=Mukesh; Type=Text; Feature=On",
            "slow apply completes",
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Add_field_creates_at_most_three_dynamic_fields_that_raise_events()
    {
        for (var i = 0; i < 4; i++) _app.Invoke("addFieldButton");

        TestAppSession.WaitUntil(() => _app.TryFind("dynamicTextBox3") is not null, "third dynamic field exists");
        Assert.Null(_app.TryFind("dynamicTextBox4"));

        _app.SetValue("dynamicTextBox1", "hello");
        TestAppSession.WaitUntil(() => _app.Text("eventLogLabel").Contains("dynamicTextBox1.TextChanged"), "dynamic field raised TextChanged");
    }
}
