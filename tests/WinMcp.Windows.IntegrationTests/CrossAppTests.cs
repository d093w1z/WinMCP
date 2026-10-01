using System.Diagnostics;
using System.Runtime.InteropServices;
using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>
/// M10: the same agent-level scenarios against the WinForms and the MFC test app. Only the locators differ
/// (<see cref="IAppUnderTest.Locate"/>); statuses and event names are shared by both apps' contract.
/// </summary>
public abstract class CrossAppTests<TApp> : IDisposable
    where TApp : class, IAppUnderTest
{
    private const string GoldenStatus = "Status: Applied: Name=Mukesh; Type=HTML; Feature=On";

    protected CrossAppTests(TApp app)
    {
        App = app;
        if (app.SkipReason is not null)
            return;
        Services = new WinMcpServices(app.ControlModeArguments(Path.Combine(Path.GetTempPath(), $"winmcp-audit-{Guid.NewGuid():N}")));
        CloseStrayDialogs();
        Act("cancel", new ElementAction.Invoke()).GetAwaiter().GetResult();
        WaitText("events", "Events: 0 []").GetAwaiter().GetResult();
        WaitText("status", "Status: Ready").GetAwaiter().GetResult();
    }

    protected TApp App { get; }

    private protected WinMcpServices Services { get; } = null!;

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    protected string Hwnd => App.Handle.ToString();

    public void Dispose()
    {
        if (App.SkipReason is not null)
            return;
        CloseStrayDialogs();
        Services.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Called first by every test: skips when the app isn't available (the MFC app needs a C++ build).</summary>
    protected void RequireApp() => Assert.SkipWhen(App.SkipReason is not null, App.SkipReason ?? "");

    protected Task<ActionResult> Act(string control, ElementAction action, string? hwnd = null) =>
        Act(App.Locate(control), action, hwnd);

    protected Task<ActionResult> Act(ElementLocator locator, ElementAction action, string? hwnd = null) =>
        Services.Interaction.PerformAsync(hwnd ?? Hwnd, null, locator, action, Token);

    protected Task<WaitResult> WaitText(string control, string text, WaitCondition condition = WaitCondition.TextEquals, int timeoutMs = 5000) =>
        Services.Tree.WaitAsync(Hwnd, null, App.Locate(control), condition, text, timeoutMs, Token);

    protected async Task<string> Text(string control) => (await Services.Tree.InspectAsync(Hwnd, null, App.Locate(control), Token)).Name;

    private async Task ShowTree()
    {
        if (App.TreeTab is { } tab)
            await Act(tab.Tabs, new ElementAction.Select(tab.Tab));
    }

    [Fact]
    public async Task Golden_scenario()
    {
        RequireApp();

        var name = await Act("name", new ElementAction.SetValue("Mukesh"));
        var type = await Act("type", new ElementAction.Select("HTML"));
        var feature = await Act("enable", new ElementAction.SetToggle(true));
        var apply = await Act("apply", new ElementAction.Invoke());

        await WaitText("status", GoldenStatus);
        Assert.Equal("Mukesh", name.ValueAfter);
        Assert.Equal("HTML", type.ValueAfter);
        Assert.False(feature.Changed); // already on
        Assert.Equal("win32.BM_CLICK", apply.Method);
        var events = await Text("events");
        Assert.Contains("nameTextBox.TextChanged", events);
        Assert.Contains("typeComboBox.SelectedIndexChanged", events);
    }

    [Fact]
    public async Task Disabled_button_is_refused_until_its_precondition_holds()
    {
        RequireApp();

        var refused = await Assert.ThrowsAsync<WinMcpException>(() => Act("advanced", new ElementAction.Invoke()));
        Assert.Equal(WinMcpErrorCode.ElementDisabled, refused.Error.Code);

        await Act("name", new ElementAction.SetValue("x"));
        await Services.Tree.WaitAsync(Hwnd, null, App.Locate("advanced"), WaitCondition.Enabled, null, 3000, Token);
        await Act("advanced", new ElementAction.Invoke());

        await WaitText("status", "Status: Advanced options opened");
    }

    [Fact]
    public async Task Unknown_option_lists_the_available_ones()
    {
        RequireApp();

        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Act("type", new ElementAction.Select("PDF")));

        Assert.Equal(WinMcpErrorCode.OptionNotFound, ex.Error.Code);
        Assert.Equal("Text", (await Services.Tree.InspectAsync(Hwnd, null, App.Locate("type"), Token)).Value);
    }

    [Fact]
    public async Task Toggle_is_idempotent_and_reported()
    {
        RequireApp();

        var off = await Act("enable", new ElementAction.SetToggle(false));
        var again = await Act("enable", new ElementAction.SetToggle(false));

        Assert.Equal((true, "off"), (off.Changed, off.StateAfter));
        Assert.False(again.Changed);
        Assert.Contains("enableCheckBox.CheckedChanged", await Text("events"));
    }

    [Fact]
    public async Task Dynamically_added_field_can_be_found_and_filled()
    {
        RequireApp();

        await Act("addField", new ElementAction.Invoke());
        await Services.Tree.WaitAsync(Hwnd, null, App.Locate("dynamic1"), WaitCondition.Exists, null, 3000, Token);
        var result = await Act("dynamic1", new ElementAction.SetValue("added"));

        Assert.Equal("added", result.ValueAfter);
        Assert.Contains("dynamicTextBox1.TextChanged", await Text("events"));
    }

    [Fact]
    public async Task Slow_apply_is_awaited_with_wait_for()
    {
        RequireApp();

        await Act("slowApply", new ElementAction.Invoke());
        Assert.Equal("Status: Applying...", await Text("status"));

        var done = await WaitText("status", "Status: Applied:", WaitCondition.TextContains);
        Assert.True(done.ElapsedMs > 500, $"waited only {done.ElapsedMs} ms");
    }

    [Fact]
    public async Task List_rows_are_folded_and_selectable_by_text()
    {
        RequireApp();

        Assert.Contains("ListItem \"Beta\" #ListViewItem-1 value=\"Beta | HTML | 2 KB\"", OutlineRenderer.Render(await Services.Tree.GetTreeAsync(Hwnd, null, 12, 400, Token)));

        await Act("list", new ElementAction.Select("Beta"));

        await WaitText("status", "Status: Selected item: Beta");
        Assert.Contains("itemsListView.SelectedIndexChanged", await Text("events"));
    }

    [Fact]
    public async Task Tree_path_selection_expands_the_way_there()
    {
        RequireApp();
        await ShowTree();

        var result = await Act("tree", new ElementAction.Select("Documents > Reports > Q1.txt"));

        Assert.Equal("Documents > Reports > Q1.txt", result.ValueAfter);
        await WaitText("status", "Status: Selected node: Q1.txt");
        Assert.Contains("itemsTreeView.AfterExpand(Documents)", await Text("events"));
    }

    [Fact]
    public async Task Menu_items_work_by_name()
    {
        RequireApp();

        await Act(new ElementLocator(Name: "File", ControlType: "MenuItem"), new ElementAction.Invoke());
        await Services.Tree.WaitAsync(Hwnd, null, new ElementLocator(Name: "New", ControlType: "MenuItem"), WaitCondition.Exists, null, 3000, Token);
        await Act(new ElementLocator(Name: "New", ControlType: "MenuItem"), new ElementAction.Invoke());

        await WaitText("status", "Status: Menu: File > New");
    }

    [Fact]
    public async Task Modal_dialog_is_found_as_an_owned_window_and_operable()
    {
        RequireApp();

        var open = await Act("dialog", new ElementAction.Invoke());
        Assert.Equal("win32.BM_CLICK", open.Method);

        var dialog = Assert.Single(Services.Windows.List(new WindowFilter(TitleContains: "Confirm")).Windows);
        Assert.Equal(App.Handle, dialog.Owner);
        await Act("dialogNote", new ElementAction.SetValue("hello"), dialog.Hwnd.ToString());
        await Act("dialogOk", new ElementAction.Invoke(), dialog.Hwnd.ToString());

        await WaitText("status", "Status: Dialog OK: hello");
    }

    [Fact]
    public async Task Message_box_from_a_menu_is_operable()
    {
        RequireApp();

        await Act(new ElementLocator(Name: "Help", ControlType: "MenuItem"), new ElementAction.Invoke());
        await Services.Tree.WaitAsync(Hwnd, null, new ElementLocator(NameContains: "About", ControlType: "MenuItem"), WaitCondition.Exists, null, 3000, Token);
        await Act(new ElementLocator(NameContains: "About", ControlType: "MenuItem"), new ElementAction.Invoke());

        WindowInfo? box = null;
        TestAppSession.WaitUntil(
            () => (box = Services.Windows.List(new WindowFilter(TitleContains: "About WinMCP")).Windows.SingleOrDefault()) is not null,
            "the About box appears");
        var ok = await Act(new ElementLocator(Name: "OK", ControlType: "Button"), new ElementAction.Invoke(), box!.Hwnd.ToString());

        Assert.Equal("win32.BM_CLICK", ok.Method);
        await WaitText("status", "Status: About closed");
    }

    /// <summary>A failed dialog test must not leave a modal window blocking the shared app.</summary>
    private void CloseStrayDialogs()
    {
        foreach (var window in Services.Windows.List(new WindowFilter(Pid: App.ProcessId)).Windows.Where(w => w.Hwnd != App.Handle && w.Owner == App.Handle))
        {
            try
            {
                var cancel = Services.Windows.ChildWindows(window.Hwnd).FirstOrDefault(c => c.ControlId is 2 or 1);
                if (cancel is not null)
                    NativeWindows.PostMessage((nint)cancel.Hwnd.Value, 0x00F5, 0, 0);
            }
            catch (WinMcpException)
            {
                // closed meanwhile
            }
        }

        TestAppSession.WaitUntil(
            () => !Services.Windows.List(new WindowFilter(Pid: App.ProcessId)).Windows.Any(w => w.Owner == App.Handle),
            "stray dialogs are closed");
    }

}

internal static class NativeWindows
{
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    public static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}

[Trait("Category", "Windows")]
public sealed class WinFormsCrossAppTests(WinFormsAppUnderTest app) : CrossAppTests<WinFormsAppUnderTest>(app), IClassFixture<WinFormsAppUnderTest>;

[Trait("Category", "Windows")]
public sealed class MfcCrossAppTests(MfcAppUnderTest app) : CrossAppTests<MfcAppUnderTest>(app), IClassFixture<MfcAppUnderTest>
{
    [Fact]
    public async Task Controls_carry_their_resource_symbols()
    {
        RequireApp();

        var name = await Services.Tree.InspectAsync(Hwnd, null, new ElementLocator(AutomationId: "1000"), Token);

        Assert.Equal(("IDC_EDIT_NAME", 1000), (name.ControlSymbol, name.ControlId));
        Assert.Equal("IDC_EDIT_NAME", name.Locator.ControlSymbol);
        Assert.True(name.Locator.Unique);
        Assert.Equal("Name:", name.Name); // labelled by the preceding static, as in real dialogs
    }

    [Fact]
    public async Task Hidden_controls_are_not_exposed()
    {
        RequireApp();

        var matches = await Services.Tree.FindAsync(Hwnd, null, new ElementLocator(ControlSymbol: "IDC_EDIT_HIDDEN"), 5, Token);

        Assert.Equal(0, matches.Count);
    }

    [Fact]
    public async Task Frozen_dialog_is_reported_as_not_responding_and_recovers()
    {
        RequireApp();

        // BM_CLICK is sent with a short timeout, so invoking the freezing button returns with a warning instead of hanging.
        var timing = Stopwatch.StartNew();
        var freeze = await Act("freeze", new ElementAction.Invoke());
        Assert.Contains("still busy", freeze.Warning);
        Assert.True(timing.Elapsed < TimeSpan.FromSeconds(3), $"invoke took {timing.ElapsedMilliseconds} ms");

        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Services.Tree.GetTreeAsync(Hwnd, null, 10, 300, Token));
        Assert.Equal(WinMcpErrorCode.TargetNotResponding, ex.Error.Code);

        TestAppSession.WaitUntil(() => new Win32Desktop().AnswersMessages(App.Handle), "the app responds again", TimeSpan.FromSeconds(12));
        Assert.Null((await Services.Tree.GetTreeAsync(Hwnd, null, 10, 300, Token)).Source);
    }
}
