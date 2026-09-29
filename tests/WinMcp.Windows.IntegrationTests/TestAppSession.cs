using System.Diagnostics;
using System.Reflection;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using FlaApplication = FlaUI.Core.Application;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>
/// One TestApp process per test class (use as <c>IClassFixture&lt;TestAppSession&gt;</c>), driven with raw FlaUI.
/// Tests call <see cref="Reset"/> first so each starts from the documented initial state without relaunching.
/// </summary>
public sealed class TestAppSession : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private readonly FlaApplication _app;

    public TestAppSession()
    {
        var path = Path.GetFullPath(ExePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("TestApp executable not found. Build samples/WinMcp.TestApp first or set WINMCP_TESTAPP_PATH.", path);

        _app = FlaApplication.Launch(path, "--position 200,200");
        Window = _app.GetMainWindow(Automation, TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException("TestApp main window did not appear.");
    }

    public static string ExePath =>
        Environment.GetEnvironmentVariable("WINMCP_TESTAPP_PATH") is { Length: > 0 } overridePath
            ? overridePath
            : typeof(TestAppSession).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "TestAppPath").Value!;

    public UIA3Automation Automation { get; } = new();

    public int ProcessId => _app.ProcessId;

    public Window Window { get; }

    public AutomationElement? TryFind(string automationId) =>
        Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));

    public AutomationElement Find(string automationId) =>
        TryFind(automationId) ?? throw new InvalidOperationException($"Element #{automationId} not found.");

    /// <summary>UIA Name — for labels this is the displayed text.</summary>
    public string Text(string automationId) => Find(automationId).Properties.Name.Value;

    public string Value(string automationId) => Find(automationId).Patterns.Value.Pattern.Value.Value;

    public void SetValue(string automationId, string value) => Find(automationId).Patterns.Value.Pattern.SetValue(value);

    public void Invoke(string automationId) => Find(automationId).Patterns.Invoke.Pattern.Invoke();

    public void Toggle(string automationId) => Find(automationId).Patterns.Toggle.Pattern.Toggle();

    public ToggleState ToggleState(string automationId) => Find(automationId).Patterns.Toggle.Pattern.ToggleState.Value;

    /// <summary>
    /// WinForms DropDownList combos expose no items while collapsed and stay open after selection (M0),
    /// so: expand, select, collapse.
    /// </summary>
    public void SelectOption(string comboId, string option)
    {
        var expandCollapse = Find(comboId).Patterns.ExpandCollapse.Pattern;
        expandCollapse.Expand();
        var item = Find(comboId).FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName(option)))
            ?? throw new InvalidOperationException($"Option '{option}' not found in #{comboId}.");
        item.Patterns.SelectionItem.Pattern.Select();
        expandCollapse.Collapse();
    }

    /// <summary>Cancel restores defaults, removes dynamic fields and clears the event log (TestApp contract).</summary>
    public void Reset()
    {
        Invoke("cancelButton");
        WaitUntil(
            () => Text("eventLogLabel") == "Events: 0 []" && Text("statusLabel") == "Status: Ready",
            "TestApp is back in its initial state");
    }

    public static void WaitUntil(Func<bool> condition, string description, TimeSpan? timeout = null)
    {
        var limit = timeout ?? DefaultTimeout;
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > limit)
                Assert.Fail($"Timed out after {limit.TotalMilliseconds:F0} ms waiting until {description}.");
            Thread.Sleep(20);
        }
    }

    public void Dispose()
    {
        _app.Kill();
        _app.Dispose();
        Automation.Dispose();
    }
}
