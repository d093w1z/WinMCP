using System.Diagnostics;
using WinMcp.Core.Automation;
using WinMcp.Core.Capture;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>M9: list view, tree view, tabs, menus, a modal WinForms dialog, a Win32 message box and a custom-drawn control.</summary>
[Trait("Category", "Windows")]
public sealed class TestAppV2Tests : IClassFixture<TestAppSession>, IDisposable
{
    private readonly TestAppSession _app;
    private readonly WinMcpServices _services = WinMcpServices.Control();

    public TestAppV2Tests(TestAppSession app)
    {
        _app = app;
        _app.Reset();
    }

    public void Dispose()
    {
        CloseStrayDialogs();
        _services.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Hwnd => _app.WindowHandle.ToString();

    private Task<ActionResult> Act(ElementLocator locator, ElementAction action, string? hwnd = null) =>
        _services.Interaction.PerformAsync(hwnd ?? Hwnd, null, locator, action, Token);

    private Task<UiTree> Tree(string? hwnd = null) => _services.Tree.GetTreeAsync(hwnd ?? Hwnd, null, 12, 400, Token);

    private Task WaitStatus(string status) =>
        _services.Tree.WaitAsync(Hwnd, null, new ElementLocator(AutomationId: "statusLabel"), WaitCondition.TextEquals, status, 5000, Token);

    [Fact]
    public async Task List_rows_are_folded_and_selectable_by_text()
    {
        var outline = OutlineRenderer.Render(await Tree());
        Assert.Contains("ListItem \"Beta\" #ListViewItem-1 value=\"Beta | HTML | 2 KB\"", outline);
        Assert.DoesNotContain("ListViewSubItem", outline);

        var result = await Act(new ElementLocator(AutomationId: "itemsListView"), new ElementAction.Select("Beta"));

        Assert.Equal("uia.SelectionItemPattern", result.Method);
        await WaitStatus("Status: Selected item: Beta");
    }

    [Fact]
    public async Task Tabs_have_their_accessible_name_and_switch_by_text()
    {
        Assert.Contains("Tab \"Details\" #detailsTabs", OutlineRenderer.Render(await Tree()));

        await Act(new ElementLocator(AutomationId: "detailsTabs"), new ElementAction.Select("Tree"));

        Assert.Contains("detailsTabs.SelectedIndexChanged(Tree)", _app.Text("eventLogLabel"));
    }

    [Fact]
    public async Task Tree_path_selection_expands_the_way_there()
    {
        await Act(new ElementLocator(AutomationId: "detailsTabs"), new ElementAction.Select("Tree"));

        var bare = await Assert.ThrowsAsync<WinMcpException>(() => Act(new ElementLocator(AutomationId: "itemsTreeView"), new ElementAction.Select("Q1.txt")));
        Assert.Equal(WinMcpErrorCode.OptionNotFound, bare.Error.Code);
        Assert.Contains("Documents > Reports > Q1.txt", bare.Error.Hint);

        var result = await Act(new ElementLocator(AutomationId: "itemsTreeView"), new ElementAction.Select("Documents > Reports > Q1.txt"));

        Assert.Equal("Documents > Reports > Q1.txt", result.ValueAfter);
        await WaitStatus("Status: Selected node: Q1.txt");
    }

    [Fact]
    public async Task Set_expanded_is_idempotent_and_reveals_children()
    {
        await Act(new ElementLocator(AutomationId: "detailsTabs"), new ElementAction.Select("Tree"));
        var documents = new ElementLocator(Name: "Documents", ControlType: "TreeItem");

        var expanded = await Act(documents, new ElementAction.SetExpanded(true));
        var again = await Act(documents, new ElementAction.SetExpanded(true));

        Assert.Equal((true, "expanded"), (expanded.Changed, expanded.StateAfter));
        Assert.Equal((false, "none"), (again.Changed, again.Method));
        Assert.Equal(1, (await _services.Tree.FindAsync(Hwnd, null, new ElementLocator(Name: "Reports", ControlType: "TreeItem"), 5, Token)).Count);

        await Act(documents, new ElementAction.SetExpanded(false));
        Assert.Equal(0, (await _services.Tree.FindAsync(Hwnd, null, new ElementLocator(Name: "Reports", ControlType: "TreeItem"), 5, Token)).Count);
    }

    [Fact]
    public async Task Menu_items_work_by_name()
    {
        await Act(new ElementLocator(Name: "File", ControlType: "MenuItem"), new ElementAction.Invoke());
        await _services.Tree.WaitAsync(Hwnd, null, new ElementLocator(Name: "New", ControlType: "MenuItem"), WaitCondition.Exists, null, 3000, Token);

        await Act(new ElementLocator(Name: "New", ControlType: "MenuItem"), new ElementAction.Invoke());

        await WaitStatus("Status: Menu: File > New");
    }

    [Fact]
    public async Task Modal_dialog_opened_by_a_button_is_visible_at_once_and_operable()
    {
        var timing = Stopwatch.StartNew();
        var open = await Act(new ElementLocator(AutomationId: "dialogButton"), new ElementAction.Invoke());
        Assert.Equal("win32.BM_CLICK", open.Method);
        Assert.True(timing.Elapsed < TimeSpan.FromSeconds(1.5), $"invoke took {timing.ElapsedMilliseconds} ms");

        // No waiting needed: the click is synced before invoke returns.
        var dialog = Assert.Single(_services.Windows.List(new WindowFilter(TitleContains: "Confirm")).Windows);
        Assert.Equal(_app.WindowHandle, dialog.Owner);
        Assert.Contains(_services.Windows.Inspect(Hwnd).OwnedWindows, o => o.Title == "Confirm");

        await Act(new ElementLocator(AutomationId: "dialogNoteTextBox"), new ElementAction.SetValue("hello"), dialog.Hwnd.ToString());
        await Act(new ElementLocator(AutomationId: "dialogOkButton"), new ElementAction.Invoke(), dialog.Hwnd.ToString());

        await WaitStatus("Status: Dialog OK: hello");
    }

    [Fact]
    public async Task Message_box_that_blocks_UI_Automation_is_operable_through_the_Win32_fallback()
    {
        await Act(new ElementLocator(Name: "Help", ControlType: "MenuItem"), new ElementAction.Invoke());
        await _services.Tree.WaitAsync(Hwnd, null, new ElementLocator(NameContains: "About", ControlType: "MenuItem"), WaitCondition.Exists, null, 3000, Token);

        // WinForms runs this menu item's handler (a MessageBox) inside the UIA Invoke call.
        var about = await Act(new ElementLocator(NameContains: "About", ControlType: "MenuItem"), new ElementAction.Invoke());
        Assert.Contains("still busy", about.Warning);

        var box = Assert.Single(_services.Windows.List(new WindowFilter(TitleContains: "About WinMCP")).Windows);
        var tree = await Tree(box.Hwnd.ToString());
        Assert.Equal(UiTreeService.Win32Source, tree.Source);
        Assert.Contains(tree.Root.Children!, n => n.ControlType == "Button" && n.Name == "OK");

        var ok = await Act(new ElementLocator(Name: "OK", ControlType: "Button"), new ElementAction.Invoke(), box.Hwnd.ToString());

        Assert.Equal("win32.BM_CLICK", ok.Method);
        await WaitStatus("Status: About closed");
        Assert.Null((await Tree()).Source); // UI Automation answers again
    }

    [Fact]
    public async Task Custom_drawn_control_is_opaque_to_UIA_but_capturable()
    {
        await Act(new ElementLocator(AutomationId: "detailsTabs"), new ElementAction.Select("Canvas"));

        var canvas = await _services.Tree.InspectAsync(Hwnd, null, new ElementLocator(AutomationId: "canvasPanel"), Token);
        Assert.Equal("Pane", canvas.ControlType);
        var subtree = await _services.Tree.GetTreeAsync(null, canvas.Ref, 5, 50, Token);
        Assert.Null(subtree.Root.Children); // nothing inside is exposed

        var shot = await new ScreenshotService(_services.Windows, _services.Tree, new PrintWindowCapture())
            .CaptureAsync(Hwnd, canvas.Ref, new ElementLocator(), 0, 1280, Token);
        Assert.True(shot.Info.ImageWidth > 50);
    }

    /// <summary>A failed dialog test must not leave a modal window blocking the shared TestApp.</summary>
    private void CloseStrayDialogs()
    {
        foreach (var window in _services.Windows.List(new WindowFilter(Pid: _app.ProcessId)).Windows.Where(w => w.Hwnd != _app.WindowHandle && w.Owner == _app.WindowHandle))
        {
            try
            {
                var cancel = _services.Windows.ChildWindows(window.Hwnd).FirstOrDefault(c => c.ControlId is 2 or 1);
                if (cancel is not null)
                    PostMessage((nint)cancel.Hwnd.Value, 0x00F5, 0, 0);
            }
            catch (WinMcpException)
            {
                // closed meanwhile
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
