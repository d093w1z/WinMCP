using WinMcp.Core.Audit;
using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;
using WinMcp.Core.Policy;
using WinMcp.Core.Symbols;
using WinMcp.Testing;
using static WinMcp.Testing.FakeUiAutomation;

namespace WinMcp.Core.Tests.Automation;

internal sealed class MemoryAuditLog : IAuditLog
{
    public List<AuditEntry> Entries { get; } = [];

    public void Record(AuditEntry entry) => Entries.Add(entry);
}

public sealed class InteractionServiceTests
{
    private const string Hwnd = "hwnd:0x00000010";
    private static readonly WindowInfo Main = FakeDesktop.Window("WinMcp.TestApp", "WinMCP Test App", pid: 1, hwnd: 0x10);
    private static readonly WindowInfo Mail = FakeDesktop.Window("mail", "Inbox", pid: 2, hwnd: 0x20);

    private readonly FakeUiAutomation _automation = new();
    private readonly MemoryAuditLog _audit = new();

    public InteractionServiceTests()
    {
        _automation.Trees[Main.Hwnd] = TestAppTree().With([.. TestAppTree().Children, Element("1.20", "Edit", "PIN", "pinTextBox", password: true)]);
        _automation.Trees[Mail.Hwnd] = Element("9", "Window", "Inbox").With(Element("9.1", "Button", "Delete all", "deleteAll"));
    }

    private readonly FakeKeyboard _keyboard = new();

    private InteractionService Service(ServerMode mode = ServerMode.Control)
    {
        var options = new WinMcpOptions(mode, ["WinMcp.TestApp"]);
        var windows = new WindowQuery(new FakeDesktop(Main, Mail), new TargetPolicy(options, 999));
        var tree = new UiTreeService(windows, _automation, new ElementRegistry(), new SymbolProvider(options));
        return new InteractionService(tree, _automation, _keyboard, windows, options, _audit);
    }

    private Task<ActionResult> SendKeys(KeyInput input, ElementLocator? locator = null, string? hwnd = Hwnd, ServerMode mode = ServerMode.Control) =>
        Service(mode).SendKeysAsync(hwnd, null, locator ?? new ElementLocator(), input, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Send_keys_to_an_element_focuses_it_then_types_into_its_window()
    {
        var result = await SendKeys(new KeyInput.Text("Mukesh"), new ElementLocator(AutomationId: "nameTextBox"));

        Assert.Equal("fake.SendInput", result.Method);
        Assert.IsType<ElementAction.Focus>(Assert.Single(_automation.Performed).Action);
        Assert.Equal((Main.Hwnd, (KeyInput)new KeyInput.Text("Mukesh")), Assert.Single(_keyboard.Sent));
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(("send_keys", "ok", "Mukesh"), (entry.Tool, entry.Outcome, entry.Arguments["text"]));
    }

    [Fact]
    public async Task Send_keys_to_a_window_types_without_focusing_an_element()
    {
        await SendKeys(KeyInputParser.Parse(null, "Ctrl+A, Delete"));

        Assert.Empty(_automation.Performed);
        Assert.Equal("Ctrl+A, Delete", Assert.Single(_audit.Entries).Arguments["keys"]);
        Assert.Single(_keyboard.Sent);
    }

    [Fact]
    public async Task Send_keys_refuses_password_targets_and_a_focused_password_field()
    {
        var target = await Assert.ThrowsAsync<WinMcpException>(() => SendKeys(new KeyInput.Text("hunter2"), new ElementLocator(AutomationId: "pinTextBox")));
        Assert.Equal(WinMcpErrorCode.PasswordField, target.Error.Code);

        // Only a window given, but a password box has focus.
        _automation.Trees[Main.Hwnd] = TestAppTree().With([Element("1.20", "Edit", "PIN", "pinTextBox", password: true) with { HasKeyboardFocus = true }]);
        var focused = await Assert.ThrowsAsync<WinMcpException>(() => SendKeys(new KeyInput.Text("hunter2")));
        Assert.Equal(WinMcpErrorCode.PasswordField, focused.Error.Code);

        Assert.Empty(_keyboard.Sent);
        Assert.All(_audit.Entries, e => Assert.Equal("<redacted>", e.Arguments["text"]));
    }

    [Fact]
    public async Task Send_keys_is_refused_in_observe_mode_and_for_other_applications()
    {
        Assert.Equal(WinMcpErrorCode.OperationNotPermitted,
            (await Assert.ThrowsAsync<WinMcpException>(() => SendKeys(new KeyInput.Text("x"), mode: ServerMode.Observe))).Error.Code);
        Assert.Equal(WinMcpErrorCode.WindowNotFound,
            (await Assert.ThrowsAsync<WinMcpException>(() => SendKeys(new KeyInput.Text("x"), hwnd: "hwnd:0x00000020"))).Error.Code);
        Assert.Empty(_keyboard.Sent);
    }

    private Task<ActionResult> Perform(ElementAction action, ElementLocator locator, string? hwnd = Hwnd, ServerMode mode = ServerMode.Control) =>
        Service(mode).PerformAsync(hwnd, null, locator, action, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Performs_the_action_on_the_resolved_element_and_audits_it()
    {
        _automation.OnPerform = (_, _) => new ActionOutcome("uia.ValuePattern", true, ValueAfter: "Mukesh");

        var result = await Perform(new ElementAction.SetValue("Mukesh"), new ElementLocator(AutomationId: "nameTextBox"));

        Assert.True(result.Ok);
        Assert.Equal(("set_value", "uia.ValuePattern", "Mukesh"), (result.Action, result.Method, result.ValueAfter));
        var (key, action) = Assert.Single(_automation.Performed);
        Assert.Equal("1.2", key.RuntimeId);
        Assert.Equal(new ElementAction.SetValue("Mukesh"), action);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(("set_value", "ok", "uia.ValuePattern"), (entry.Tool, entry.Outcome, entry.Method));
        Assert.Equal("WinMcp.TestApp", entry.Process);
        Assert.Equal("Edit \"Name:\" #nameTextBox", entry.Target);
        Assert.Equal("Mukesh", entry.Arguments["value"]);
    }

    [Fact]
    public async Task Disabled_element_is_refused_before_reaching_the_target()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Perform(new ElementAction.Invoke(), new ElementLocator(AutomationId: "advancedButton")));

        Assert.Equal(WinMcpErrorCode.ElementDisabled, ex.Error.Code);
        Assert.Empty(_automation.Performed);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(("ELEMENT_DISABLED", null), (entry.Outcome, entry.Method));
    }

    [Fact]
    public async Task Password_fields_are_refused_and_the_value_is_redacted_in_the_audit_log()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Perform(new ElementAction.SetValue("hunter2"), new ElementLocator(AutomationId: "pinTextBox")));

        Assert.Equal(WinMcpErrorCode.PasswordField, ex.Error.Code);
        Assert.Empty(_automation.Performed);
        Assert.Equal("<redacted>", Assert.Single(_audit.Entries).Arguments["value"]);
    }

    [Fact]
    public async Task Observe_mode_refuses_even_if_a_control_tool_were_reachable()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() =>
            Perform(new ElementAction.Invoke(), new ElementLocator(AutomationId: "applyButton"), mode: ServerMode.Observe));

        Assert.Equal(WinMcpErrorCode.OperationNotPermitted, ex.Error.Code);
        Assert.Empty(_automation.Performed);
        Assert.Equal("OPERATION_NOT_PERMITTED", Assert.Single(_audit.Entries).Outcome);
    }

    [Fact]
    public async Task Non_allowlisted_window_is_not_found_and_untouched()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() =>
            Perform(new ElementAction.Invoke(), new ElementLocator(AutomationId: "deleteAll"), hwnd: "hwnd:0x00000020"));

        Assert.Equal(WinMcpErrorCode.WindowNotFound, ex.Error.Code);
        Assert.Empty(_automation.Performed);
        var entry = Assert.Single(_audit.Entries);
        Assert.Null(entry.Process);
        Assert.Null(entry.Target);
    }

    [Fact]
    public async Task Ambiguous_criteria_never_guess_a_target()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Perform(new ElementAction.Invoke(), new ElementLocator(ControlType: "Button")));

        Assert.Equal(WinMcpErrorCode.AmbiguousMatch, ex.Error.Code);
        Assert.Empty(_automation.Performed);
    }

    [Fact]
    public async Task Errors_from_the_target_propagate_and_are_audited()
    {
        _automation.OnPerform = (_, _) => throw new WinMcpException(new WinMcpError(WinMcpErrorCode.OptionNotFound, "no 'PDF'"));

        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Perform(new ElementAction.Select("PDF"), new ElementLocator(AutomationId: "typeComboBox")));

        Assert.Equal(WinMcpErrorCode.OptionNotFound, ex.Error.Code);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(("OPTION_NOT_FOUND", "PDF"), (entry.Outcome, entry.Arguments["option"]));
    }
}

public sealed class WaitForTests
{
    private const string Hwnd = "hwnd:0x00000010";
    private static readonly WindowInfo Main = FakeDesktop.Window("WinMcp.TestApp", "WinMCP Test App", pid: 1, hwnd: 0x10);
    private readonly FakeUiAutomation _automation = new();
    private readonly UiTreeService _service;

    public WaitForTests()
    {
        _automation.Trees[Main.Hwnd] = TestAppTree();
        var options = new WinMcpOptions(ServerMode.Observe, ["WinMcp.TestApp"]);
        _service = new UiTreeService(new WindowQuery(new FakeDesktop(Main), new TargetPolicy(options, 999)), _automation, new ElementRegistry(), new SymbolProvider(options));
    }

    private Task<WaitResult> Wait(ElementLocator locator, WaitCondition condition, string? text = null, int timeoutMs = 2000, string? element = null) =>
        _service.WaitAsync(element is null ? Hwnd : null, element, locator, condition, text, timeoutMs, TestContext.Current.CancellationToken);

    /// <summary>Replaces the status label text after a delay, like the TestApp's Slow apply.</summary>
    private void ChangeStatusLater(string status, int delayMs = 300) =>
        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs, TestContext.Current.CancellationToken);
            var tree = TestAppTree();
            _automation.Trees[Main.Hwnd] = tree with
            {
                Children = [.. tree.Children.Select(c => c.AutomationId == "statusLabel" ? c with { Name = status } : c)],
            };
        }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Text_contains_succeeds_once_the_ui_changes()
    {
        ChangeStatusLater("Status: Applied: Name=Mukesh; Type=HTML; Feature=On");

        var result = await Wait(new ElementLocator(AutomationId: "statusLabel"), WaitCondition.TextContains, "Applied");

        Assert.True(result.Ok);
        Assert.Equal("text_contains", result.Condition);
        Assert.Equal("Status: Applied: Name=Mukesh; Type=HTML; Feature=On", result.Element!.Name);
        Assert.InRange(result.ElapsedMs, 200, 1900);
    }

    [Fact]
    public async Task Already_true_returns_immediately() =>
        Assert.True((await Wait(new ElementLocator(AutomationId: "statusLabel"), WaitCondition.TextEquals, "Status: Ready")).ElapsedMs < 200);

    [Fact]
    public async Task Timeout_is_retryable_and_reports_the_last_text()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() =>
            Wait(new ElementLocator(AutomationId: "statusLabel"), WaitCondition.TextContains, "Applied", timeoutMs: 300));

        Assert.Equal(WinMcpErrorCode.Timeout, ex.Error.Code);
        Assert.True(ex.Error.Retryable);
        Assert.Equal("Status: Ready", ex.Error.Details!["last_text"]);
    }

    [Fact]
    public async Task Exists_waits_for_an_element_to_appear()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(300, TestContext.Current.CancellationToken);
            var tree = TestAppTree();
            _automation.Trees[Main.Hwnd] = tree.With([.. tree.Children, Element("1.30", "Edit", "Dynamic 1:", "dynamicTextBox1")]);
        }, TestContext.Current.CancellationToken);

        var result = await Wait(new ElementLocator(AutomationId: "dynamicTextBox1"), WaitCondition.Exists);

        Assert.Equal("dynamicTextBox1", result.Element!.AutomationId);
    }

    [Fact]
    public async Task Gone_is_satisfied_when_a_ref_d_element_disappears()
    {
        var apply = (await _service.FindAsync(Hwnd, null, new ElementLocator(AutomationId: "applyButton"), 1, TestContext.Current.CancellationToken)).Matches[0].Ref;
        _automation.Trees[Main.Hwnd] = Element("1", "Window", "WinMCP Test App");

        var result = await Wait(new ElementLocator(), WaitCondition.Gone, element: apply);

        Assert.True(result.Ok);
        Assert.Null(result.Element);
    }

    [Fact]
    public async Task Enabled_requires_the_element_to_be_enabled()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() =>
            Wait(new ElementLocator(AutomationId: "advancedButton"), WaitCondition.Enabled, timeoutMs: 200));

        Assert.Equal(WinMcpErrorCode.Timeout, ex.Error.Code);
    }

    [Fact]
    public async Task Text_conditions_need_exactly_one_element() =>
        Assert.Equal(WinMcpErrorCode.AmbiguousMatch,
            (await Assert.ThrowsAsync<WinMcpException>(() => Wait(new ElementLocator(ControlType: "Button"), WaitCondition.TextEquals, "Apply"))).Error.Code);

    [Fact]
    public async Task Password_fields_cannot_be_probed_by_text()
    {
        _automation.Trees[Main.Hwnd] = Element("1", "Window", "App").With(Element("1.1", "Edit", "PIN", "pin", value: "1234", password: true));

        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Wait(new ElementLocator(AutomationId: "pin"), WaitCondition.TextEquals, "1234"));

        Assert.Equal(WinMcpErrorCode.PasswordField, ex.Error.Code);
    }

    [Theory]
    [InlineData(WaitCondition.TextEquals, null, 1000)]
    [InlineData(WaitCondition.Exists, null, 60_001)]
    [InlineData(WaitCondition.Exists, null, -1)]
    public async Task Invalid_arguments_are_rejected(WaitCondition condition, string? text, int timeoutMs) =>
        Assert.Equal(WinMcpErrorCode.InvalidArgument,
            (await Assert.ThrowsAsync<WinMcpException>(() => Wait(new ElementLocator(AutomationId: "statusLabel"), condition, text, timeoutMs))).Error.Code);

    [Theory]
    [InlineData("text_contains", WaitCondition.TextContains)]
    [InlineData(" GONE ", WaitCondition.Gone)]
    public void Conditions_parse_from_wire_names(string wire, WaitCondition expected) =>
        Assert.Equal(expected, ActionArguments.ParseCondition(wire));

    [Theory]
    [InlineData("on", true)]
    [InlineData("OFF", false)]
    [InlineData("checked", true)]
    public void Toggle_states_parse(string wire, bool expected) =>
        Assert.Equal(expected, ActionArguments.ParseToggle(wire));
}

public sealed class JsonlAuditLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmcp-audit-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Appends_one_snake_case_json_line_per_entry_to_a_daily_file()
    {
        var log = new JsonlAuditLog(_directory);
        var at = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

        log.Record(new AuditEntry(at, "invoke", "hwnd:0x00000010", "WinMcp.TestApp", "e7", "Button \"Apply\"", new Dictionary<string, object?>(), "ok", "uia.InvokePattern", 12));
        log.Record(new AuditEntry(at, "set_value", null, null, null, null, new Dictionary<string, object?> { ["value"] = "x" }, "WINDOW_NOT_FOUND", null, 3));

        var lines = File.ReadAllLines(Path.Combine(_directory, "audit-20260930.jsonl"));
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"tool\":\"invoke\"", lines[0]);
        Assert.Contains("\"method\":\"uia.InvokePattern\"", lines[0]);
        Assert.Contains("\"elapsed_ms\":12", lines[0]);
        Assert.DoesNotContain("\"method\"", lines[1]); // null omitted
    }
}
