using System.Globalization;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;
using WinMcp.Core.Mfc;
using WinMcp.Core.Native;
using WinMcp.Core.Symbols;

namespace WinMcp.Core.Automation;

/// <summary>
/// Backs <c>get_ui_tree</c>, <c>find_elements</c> and <c>inspect_element</c>: window gatekeeping, normalization,
/// refs, limits, locator resolution and <c>resource.h</c> symbols.
/// </summary>
/// <param name="native">Framework detection (M11): MFC apps get MFC's standard symbols and <c>mfc_class_guess</c>.</param>
public sealed class UiTreeService(WindowQuery windows, IUiAutomation automation, ElementRegistry registry, SymbolProvider symbols, NativeAppInfo? native = null)
{
    public const int DefaultMaxDepth = 10;
    public const int DefaultMaxNodes = 300;
    public const int DefaultMaxResults = 25;
    private const int MaxValueLength = 200;
    private const int MaxAmbiguousCandidates = 10;

    /// <summary>The window being worked on, its normalized tree, and the subtree root the caller asked for.</summary>
    /// <param name="Source">Null for UI Automation; <see cref="Win32Source"/> when built from child windows.</param>
    /// <param name="Mfc">The window's application uses MFC.</param>
    private sealed record Scope(WindowDetails Window, RawElement Tree, RawElement Root, SymbolTable? Symbols, string? Source, bool Mfc)
    {
        public WindowHandle Handle => Window.Window.Hwnd;

        private Dictionary<string, RawElement>? _parents;

        /// <summary>Parent of an element in the (normalized) tree; null for the root.</summary>
        public RawElement? ParentOf(RawElement e)
        {
            if (_parents is null)
            {
                _parents = [];
                foreach (var node in Tree.DescendantsAndSelf())
                    foreach (var child in node.Children)
                        _parents[child.RuntimeId] = node;
            }
            return _parents.GetValueOrDefault(e.RuntimeId);
        }
    }

    public async Task<UiTree> GetTreeAsync(string? hwnd, string? element, int maxDepth, int maxNodes, CancellationToken cancellationToken)
    {
        RequireRange(maxDepth, 1, 50, "max_depth");
        RequireRange(maxNodes, 1, 2000, "max_nodes");

        var scope = await ResolveScopeAsync(hwnd, element, cancellationToken);

        var count = 0;
        string? truncationReason = null;

        UiNode Build(RawElement raw, int depth)
        {
            count++;
            // Assign before recursing so new refs follow reading order (window e1, then its children).
            var reference = Ref(scope, raw);
            var children = new List<UiNode>();
            var omitted = 0;
            foreach (var child in raw.Children)
            {
                if (depth >= maxDepth || count >= maxNodes)
                {
                    omitted++;
                    truncationReason ??= depth >= maxDepth ? "max_depth" : "max_nodes";
                    continue;
                }
                children.Add(Build(child, depth + 1));
            }
            var (value, states) = Describe(raw);
            return new UiNode(
                reference,
                raw.ControlType,
                raw.Name,
                NullIfEmpty(raw.AutomationId),
                value,
                states,
                children.Count > 0 ? children : null,
                omitted > 0 ? omitted : null,
                SymbolFor(scope, raw).Symbol);
        }

        var tree = Build(scope.Root, 0);
        return new UiTree(scope.Handle, tree, count, truncationReason is not null, truncationReason, scope.Source);
    }

    public async Task<ElementMatches> FindAsync(string? hwnd, string? element, ElementLocator locator, int maxResults, CancellationToken cancellationToken)
    {
        RequireRange(maxResults, 1, 500, "max_results");
        if (locator.IsEmpty)
            throw Invalid("Give at least one criterion (automation_id, control_symbol, name, name_contains, control_type or class_name).",
                "To see every element, use get_ui_tree.");
        ValidateControlType(locator);

        var scope = await ResolveScopeAsync(hwnd, element, cancellationToken);
        var all = Search(scope, scope.Root, locator);
        var matches = all.Take(maxResults).Select(e =>
        {
            var (value, states) = Describe(e);
            return new ElementMatch(Ref(scope, e), e.ControlType, e.Name, NullIfEmpty(e.AutomationId), value, states);
        }).ToList();
        return new ElementMatches(matches, all.Count, all.Count > matches.Count, scope.Source);
    }

    public async Task<ElementDetail> InspectAsync(string? hwnd, string? element, ElementLocator locator, CancellationToken cancellationToken)
    {
        var (scope, target, reference) = await ResolveTargetAsync(hwnd, element, locator, cancellationToken);
        var extras = await automation.GetElementExtrasAsync(new ElementKey(scope.Handle, target.RuntimeId), cancellationToken);

        var path = PathTo(scope.Tree, target.RuntimeId)!; // target came from this tree
        var host = path.LastOrDefault(e => e.NativeWindowHandle != 0)?.NativeWindowHandle ?? scope.Handle.Value;
        var parent = path.Count > 1 ? Ref(scope, path[^2]) : null;
        var labeledBy = extras.LabeledByRuntimeId is { } labelId && scope.Tree.DescendantsAndSelf().FirstOrDefault(e => e.RuntimeId == labelId) is { } label
            ? Ref(scope, label)
            : null;
        // Symbols only where UIA exposes the control ID as automation id (dialog controls, menu commands): control IDs
        // are unique among siblings only, so an ID inside another control (a property grid's header is 1) isn't IDOK (M11).
        var symbol = SymbolFor(scope, target);
        var mfcClass = scope.Mfc && target.NativeWindowHandle != 0 ? MfcClassGuess.Guess(target.ClassName, extras.ControlId, target.NativeWindowHandle == scope.Handle.Value) : null;
        var (value, states) = Describe(target);

        return new ElementDetail(
            reference,
            target.ControlType,
            target.Name,
            NullIfEmpty(target.AutomationId),
            value,
            states,
            target.ClassName,
            NullIfEmpty(extras.FrameworkId),
            target.NativeWindowHandle != 0 ? new WindowHandle(target.NativeWindowHandle) : null,
            new WindowHandle(host),
            extras.ControlId,
            symbol.Symbol,
            symbol.Candidates,
            target.IsEnabled,
            target.IsOffscreen,
            extras.IsKeyboardFocusable,
            target.HasKeyboardFocus,
            extras.Patterns,
            target.IsPassword ? null : extras.Options,
            target.IsPassword ? null : extras.OptionCount ?? extras.Options?.Count,
            extras.Bounds,
            parent,
            labeledBy,
            NullIfEmpty(extras.HelpText),
            SuggestLocator(scope, target, symbol.Symbol),
            mfcClass);
    }

    /// <summary>The element an action targets, freshly fetched, with the window it belongs to.</summary>
    public sealed record ResolvedElement(string Ref, ElementKey Key, RawElement Element, WindowInfo Window);

    /// <summary>Resolves the target of an interaction tool with the same rules (and allowlist gate) as inspect_element.</summary>
    public async Task<ResolvedElement> ResolveElementAsync(string? hwnd, string? element, ElementLocator locator, CancellationToken cancellationToken)
    {
        var (scope, target, reference) = await ResolveTargetAsync(hwnd, element, locator, cancellationToken);
        return new ResolvedElement(reference, new ElementKey(scope.Handle, target.RuntimeId), target, scope.Window.Window);
    }

    /// <summary>The element with keyboard focus in the window, if any (fetched fresh).</summary>
    public async Task<RawElement?> FocusedElementAsync(WindowHandle window, CancellationToken cancellationToken)
    {
        var (tree, _) = await FetchAsync(window, cancellationToken);
        return tree.DescendantsAndSelf().LastOrDefault(e => e.HasKeyboardFocus);
    }

    /// <summary>Live screen bounds of a resolved element (trees don't carry bounds; see <see cref="ElementExtras.Bounds"/>).</summary>
    public async Task<Rect> BoundsAsync(ResolvedElement element, CancellationToken cancellationToken) =>
        (await automation.GetElementExtrasAsync(element.Key, cancellationToken)).Bounds;

    public const int DefaultWaitTimeoutMs = 5000;
    private const int MaxWaitTimeoutMs = 60_000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Polls until the condition holds or the timeout expires (TIMEOUT, retryable). Exists/Gone accept any number of
    /// matches; the other conditions need exactly one element. Text is the element's value, or its name when it has
    /// no value; password fields can't be waited on by text (that would let callers probe their content).
    /// </summary>
    public async Task<WaitResult> WaitAsync(
        string? hwnd, string? element, ElementLocator locator, WaitCondition condition, string? text, int timeoutMs, CancellationToken cancellationToken)
    {
        RequireRange(timeoutMs, 0, MaxWaitTimeoutMs, "timeout_ms");
        if (condition is WaitCondition.TextEquals or WaitCondition.TextContains && text is null)
            throw Invalid($"Condition '{Wire(condition)}' needs 'text'.");
        if (element is null && locator.IsEmpty)
            throw Invalid("Give 'element' (a ref), or 'hwnd' with criteria such as automation_id.");
        ValidateControlType(locator);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string? lastText = null;
        var lastCount = 0;
        while (true)
        {
            List<RawElement> matches;
            Scope? scope = null;
            try
            {
                scope = await ResolveScopeAsync(hwnd, element, cancellationToken);
                matches = locator.IsEmpty ? [scope.Root] : Search(scope, scope.Root, locator);
            }
            catch (WinMcpException ex) when (ex.Error.Code == WinMcpErrorCode.ElementStale)
            {
                matches = []; // the ref'd element is gone
            }

            lastCount = matches.Count;
            if (condition is not (WaitCondition.Exists or WaitCondition.Gone) && matches.Count > 1)
                throw new WinMcpException(new WinMcpError(WinMcpErrorCode.AmbiguousMatch,
                    $"{matches.Count} elements match; '{Wire(condition)}' needs exactly one.", Hint: "Add criteria or pass a ref as 'element'."));
            if (matches is [{ IsPassword: true }] && condition is WaitCondition.TextEquals or WaitCondition.TextContains)
                throw new WinMcpException(new WinMcpError(WinMcpErrorCode.PasswordField, "Password fields can't be waited on by text."));

            var single = matches.Count == 1 ? matches[0] : null;
            lastText = single is null ? null : single.Value ?? single.Name;
            var satisfied = condition switch
            {
                WaitCondition.Exists => matches.Count > 0,
                WaitCondition.Gone => matches.Count == 0,
                WaitCondition.Enabled => single is { IsEnabled: true },
                WaitCondition.TextEquals => lastText is not null && string.Equals(lastText, text, StringComparison.Ordinal),
                WaitCondition.TextContains => lastText is not null && lastText.Contains(text!, StringComparison.Ordinal),
                _ => false,
            };
            if (satisfied)
            {
                ElementMatch? match = null;
                if (scope is not null && matches.Count > 0)
                {
                    var (value, states) = Describe(matches[0]);
                    match = new ElementMatch(Ref(scope, matches[0]), matches[0].ControlType, matches[0].Name, NullIfEmpty(matches[0].AutomationId), value, states);
                }
                return new WaitResult(true, Wire(condition), stopwatch.ElapsedMilliseconds, match);
            }

            if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                throw new WinMcpException(new WinMcpError(
                    WinMcpErrorCode.Timeout,
                    $"Condition '{Wire(condition)}' not met within {timeoutMs} ms.",
                    Hint: "The UI may still be working; retry with a longer timeout_ms, or check the criteria with get_ui_tree.",
                    Details: new Dictionary<string, object?> { ["matches"] = lastCount, ["last_text"] = lastText }));
            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static string Wire(WaitCondition condition) => condition switch
    {
        WaitCondition.Exists => "exists",
        WaitCondition.Gone => "gone",
        WaitCondition.Enabled => "enabled",
        WaitCondition.TextEquals => "text_equals",
        _ => "text_contains",
    };

    /// <summary>
    /// The single element a request designates: a ref alone; or hwnd/ref scope plus a locator that must match
    /// exactly one element.
    /// </summary>
    private async Task<(Scope Scope, RawElement Target, string Ref)> ResolveTargetAsync(
        string? hwnd, string? element, ElementLocator locator, CancellationToken cancellationToken)
    {
        if (element is null && locator.IsEmpty)
            throw Invalid("Give 'element' (a ref), or 'hwnd' with locator criteria such as automation_id.");
        ValidateControlType(locator);

        var scope = await ResolveScopeAsync(hwnd, element, cancellationToken);
        if (locator.IsEmpty)
            return (scope, scope.Root, Ref(scope, scope.Root));

        var matches = Search(scope, scope.Root, locator);
        return matches.Count switch
        {
            1 => (scope, matches[0], Ref(scope, matches[0])),
            0 => throw new WinMcpException(new WinMcpError(
                WinMcpErrorCode.ElementNotFound,
                "No element matches the given criteria.",
                Hint: "Check the criteria against get_ui_tree; names and automation ids are matched exactly (case-insensitive).")),
            _ => throw new WinMcpException(new WinMcpError(
                WinMcpErrorCode.AmbiguousMatch,
                $"{matches.Count} elements match the given criteria; exactly one is required.",
                Hint: "Add criteria (e.g. automation_id or control_type), or pass one candidate's ref as 'element'.",
                Details: new Dictionary<string, object?>
                {
                    ["candidates"] = matches.Take(MaxAmbiguousCandidates).Select(e => new Dictionary<string, object?>
                    {
                        ["ref"] = Ref(scope, e),
                        ["control_type"] = e.ControlType,
                        ["name"] = e.Name,
                        ["automation_id"] = NullIfEmpty(e.AutomationId),
                    }).ToList(),
                })),
        };
    }

    /// <summary>
    /// Resolves the window and subtree root: the whole window, or the element a ref points to. An element ref alone is
    /// enough; with both, they must agree. The window is always re-validated, so a ref can't outlive the allowlist.
    /// </summary>
    private async Task<Scope> ResolveScopeAsync(string? hwnd, string? element, CancellationToken cancellationToken)
    {
        ElementKey? key = element is null ? null : registry.Resolve(element);
        if (hwnd is null && key is null)
            throw Invalid("Give 'hwnd' (from list_windows) or 'element' (a ref from get_ui_tree/find_elements).");

        var window = windows.ResolveOperable(hwnd ?? key!.Value.Window.ToString());
        var handle = window.Window.Hwnd;
        if (key is { } k && k.Window != handle)
            throw Invalid($"Element '{element}' belongs to window {k.Window}, not {handle}.");

        var (fetched, source) = await FetchAsync(handle, cancellationToken);
        var tree = TreeNormalizer.Normalize(fetched);
        var mfc = native?.IsMfc(window) ?? false;
        var table = symbols.ForProcess(window.Window.Process.Name, mfc);
        if (key is null)
            return new Scope(window, tree, tree, table, source, mfc);

        var root = tree.DescendantsAndSelf().FirstOrDefault(e => e.RuntimeId == key.Value.RuntimeId)
            ?? throw new WinMcpException(new WinMcpError(
                WinMcpErrorCode.ElementStale,
                $"Element '{element}' no longer exists in window {handle}.",
                Hint: "The UI changed. Call get_ui_tree or find_elements again for current refs."));
        return new Scope(window, tree, root, table, source, mfc);
    }

    public const string Win32Source = "win32";

    /// <summary>
    /// UIA timing out on a window Windows still considers responsive means UIA itself can't answer: the tree is too
    /// large to fetch in time (M4), or UIA is blocked for the application (M9: a modal dialog opened from inside a UIA
    /// call). Either way the Win32 controls still answer messages, so fall back to a tree of child windows (plan §A.5).
    /// </summary>
    private async Task<(RawElement Tree, string? Source)> FetchAsync(WindowHandle window, CancellationToken cancellationToken)
    {
        try
        {
            return (await automation.GetWindowTreeAsync(window, cancellationToken), null);
        }
        catch (WinMcpException ex) when (ex.Error.Code == WinMcpErrorCode.TargetNotResponding && IsResponding(window))
        {
            var info = windows.ResolveTopLevel(window.ToString()).Window;
            var children = windows.ChildWindows(window);
            if (children.Count > 0)
                return (Win32TreeBuilder.Build(info, children), Win32Source);
            throw new WinMcpException(ex.Error with
            {
                Message = $"UI Automation for {window} timed out although the application is responding, and it has no Win32 child controls to fall back to.",
                Hint = "The UI tree may be too large to fetch in time, or UI Automation is blocked by a modal dialog. "
                       + "Check list_windows for a dialog of this application and act on that.",
            }, ex);
        }
    }

    /// <summary>
    /// Hung (not processing messages) vs. UIA-blocked-but-pumping (M9: a modal dialog opened inside a UIA call) need
    /// different answers, so ask the window directly — Windows' own hung flag lags ~5 s behind a real freeze.
    /// </summary>
    private bool IsResponding(WindowHandle window)
    {
        try { return windows.ResolveTopLevel(window.ToString()).Responding && windows.AnswersMessages(window); }
        catch (WinMcpException) { return false; } // closed meanwhile: keep the original error
    }

    private List<RawElement> Search(Scope scope, RawElement root, ElementLocator locator)
    {
        var symbolId = ResolveSymbol(scope, locator.ControlSymbol);
        return root.DescendantsAndSelf().Where(e => Matches(e, locator, symbolId)).ToList();
    }

    /// <summary>Win32/MFC controls expose their control ID as AutomationId (M0 finding 3), so symbols match on it.</summary>
    private int? ResolveSymbol(Scope scope, string? controlSymbol)
    {
        if (controlSymbol is null)
            return null;
        var process = scope.Window.Window.Process.Name;
        if (scope.Symbols is not { } table)
            throw Invalid($"control_symbol needs a resource.h, and none is configured for '{process}'.",
                symbols.HasSymbols(process)
                    ? "The configured resource.h could not be read."
                    : $"The user can start WinMCP with --symbols {process}=<path-to-resource.h>. Use automation_id (the numeric control ID) meanwhile.");
        return table.ControlId(controlSymbol)
            ?? throw Invalid(symbols.HasSymbols(process)
                ? $"'{controlSymbol}' is not a control or command symbol in the resource.h configured for '{process}'."
                : $"'{controlSymbol}' is not one of MFC's standard IDs, and no resource.h is configured for '{process}'.");
    }

    private static bool Matches(RawElement e, ElementLocator locator, int? symbolId) =>
        (locator.AutomationId is null || string.Equals(e.AutomationId, locator.AutomationId, StringComparison.OrdinalIgnoreCase))
        && (symbolId is null || ParseId(e.AutomationId) == symbolId)
        && (locator.Name is null || string.Equals(e.Name, locator.Name, StringComparison.OrdinalIgnoreCase))
        && (locator.NameContains is null || e.Name.Contains(locator.NameContains, StringComparison.OrdinalIgnoreCase))
        && (locator.ControlType is null || string.Equals(e.ControlType, locator.ControlType, StringComparison.OrdinalIgnoreCase))
        && (locator.ClassName is null || string.Equals(e.ClassName, locator.ClassName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Most durable first: automation id, then resource.h symbol, then name + control type. Uniqueness is checked
    /// against the current window; if nothing is unique, the best candidate is returned with <c>Unique = false</c>.
    /// </summary>
    private SuggestedLocator SuggestLocator(Scope scope, RawElement target, string? symbol)
    {
        // A symbol beats the numeric automation id it was derived from: it says what the control is and survives renumbering.
        var candidates = new List<SuggestedLocator>();
        if (symbol is not null)
            candidates.Add(new SuggestedLocator(null, symbol, null, null, false));
        if (target.AutomationId.Length > 0)
            candidates.Add(new SuggestedLocator(target.AutomationId, null, null, null, false));
        if (target.Name.Length > 0)
            candidates.Add(new SuggestedLocator(null, null, target.Name, target.ControlType, false));
        if (target.AutomationId.Length > 0)
            candidates.Add(new SuggestedLocator(target.AutomationId, null, null, target.ControlType, false));

        foreach (var candidate in candidates)
        {
            var locator = new ElementLocator(AutomationId: candidate.AutomationId, Name: candidate.Name, ControlType: candidate.ControlType, ControlSymbol: candidate.ControlSymbol);
            if (Search(scope, scope.Tree, locator).Count == 1)
                return candidate with { Unique = true };
        }
        return candidates.FirstOrDefault() ?? new SuggestedLocator(null, null, null, target.ControlType, false);
    }

    private string Ref(Scope scope, RawElement e) => registry.GetOrAdd(new ElementKey(scope.Handle, e.RuntimeId));

    private static SymbolMatch SymbolFor(Scope scope, RawElement e) =>
        ParseId(e.AutomationId) is { } id ? SymbolFor(scope, e, id) : SymbolMatch.None;

    /// <summary>
    /// Menu items carry command IDs (<c>ID_*</c>), everything else control IDs. Windows' standard dialog IDs (<c>IDOK</c>,
    /// <c>IDCANCEL</c>, <c>IDC_STATIC</c>) apply only to controls of a dialog: elsewhere 1 and 2 are just small IDs
    /// (BCGControlBar's internal windows in a real-world application showed as IDOK/IDCANCEL, M11). Within a dialog,
    /// <c>IDOK</c>…<c>IDCONTINUE</c> name buttons only — a page container numbered 1 isn't IDOK — and no standard ID
    /// names a dialog itself.
    /// </summary>
    private static SymbolMatch SymbolFor(Scope scope, RawElement e, int id) =>
        scope.Symbols is not { } table ? SymbolMatch.None
        : e.ControlType == "MenuItem" ? table.LookupCommand(id)
        : table.LookupControl(id, standardDialogIds: StandardIdsApply(scope, e, id));

    private static bool StandardIdsApply(Scope scope, RawElement e, int id) =>
        scope.ParentOf(e)?.ClassName == "#32770"
        && e.ClassName != "#32770"
        && (id is -1 or 0xFFFF || e.ControlType == "Button"); // IDC_STATIC for any control; IDOK… for buttons

    /// <returns>Root-to-target chain of elements, or null when the target isn't in the tree.</returns>
    private static List<RawElement>? PathTo(RawElement node, string runtimeId)
    {
        if (node.RuntimeId == runtimeId)
            return [node];
        foreach (var child in node.Children)
        {
            if (PathTo(child, runtimeId) is { } path)
            {
                path.Insert(0, node);
                return path;
            }
        }
        return null;
    }

    private static void ValidateControlType(ElementLocator locator)
    {
        if (locator.ControlType is { } type && !ControlTypes.All.Contains(type))
            throw Invalid($"'{type}' is not a UI Automation control type.",
                ControlTypes.CommonMistakes.TryGetValue(type, out var suggestion)
                    ? $"Did you mean '{suggestion}'?"
                    : $"Valid types: {string.Join(", ", ControlTypes.All)}.");
    }

    /// <summary>Agent-facing value and non-default states. Password values are never returned.</summary>
    private static (string? Value, IReadOnlyList<string>? States) Describe(RawElement e)
    {
        var states = new List<string>();
        if (!e.IsEnabled) states.Add("disabled");
        if (e.IsOffscreen) states.Add("offscreen");
        if (e.HasKeyboardFocus) states.Add("focused");
        if (e.IsPassword) states.Add("password");
        if (e.ExpandCollapseState is { } expand && expand != "leaf_node") states.Add(expand);
        if (e.ToggleState is { } toggle) states.Add(toggle);
        if (e.IsSelected == true) states.Add("selected");

        var value = e.IsPassword || e.Value is null ? null
            : e.Value.Length > MaxValueLength ? e.Value[..MaxValueLength] + "…"
            : e.Value;
        return (value, states.Count > 0 ? states : null);
    }

    private static int? ParseId(string automationId) =>
        int.TryParse(automationId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id) ? id : null;

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    private static void RequireRange(int value, int min, int max, string name)
    {
        if (value < min || value > max)
            throw Invalid($"{name} must be between {min} and {max}, got {value}.");
    }

    private static WinMcpException Invalid(string message, string? hint = null) =>
        new(new WinMcpError(WinMcpErrorCode.InvalidArgument, message, hint));
}
