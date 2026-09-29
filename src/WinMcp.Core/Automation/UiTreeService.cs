using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;

namespace WinMcp.Core.Automation;

/// <summary>Backs <c>get_ui_tree</c> and <c>find_elements</c>: window gatekeeping, normalization, refs and limits.</summary>
public sealed class UiTreeService(WindowQuery windows, IUiAutomation automation, ElementRegistry registry)
{
    public const int DefaultMaxDepth = 10;
    public const int DefaultMaxNodes = 300;
    public const int DefaultMaxResults = 25;
    private const int MaxValueLength = 200;

    public async Task<UiTree> GetTreeAsync(string? hwnd, string? element, int maxDepth, int maxNodes, CancellationToken cancellationToken)
    {
        RequireRange(maxDepth, 1, 50, "max_depth");
        RequireRange(maxNodes, 1, 2000, "max_nodes");

        var (window, root) = await ResolveRootAsync(hwnd, element, cancellationToken);

        var count = 0;
        string? truncationReason = null;

        UiNode Build(RawElement raw, int depth)
        {
            count++;
            // Assign before recursing so new refs follow reading order (window e1, then its children).
            var reference = registry.GetOrAdd(new ElementKey(window, raw.RuntimeId));
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
                omitted > 0 ? omitted : null);
        }

        var tree = Build(root, 0);
        return new UiTree(window, tree, count, truncationReason is not null, truncationReason);
    }

    public async Task<ElementMatches> FindAsync(string? hwnd, string? element, ElementLocator locator, int maxResults, CancellationToken cancellationToken)
    {
        RequireRange(maxResults, 1, 500, "max_results");
        if (locator.IsEmpty)
            throw Invalid("Give at least one criterion (automation_id, name, name_contains, control_type or class_name).",
                "To see every element, use get_ui_tree.");
        if (locator.ControlType is { } type && !ControlTypes.All.Contains(type))
            throw Invalid($"'{type}' is not a UI Automation control type.",
                ControlTypes.CommonMistakes.TryGetValue(type, out var suggestion)
                    ? $"Did you mean '{suggestion}'?"
                    : $"Valid types: {string.Join(", ", ControlTypes.All)}.");

        var (window, root) = await ResolveRootAsync(hwnd, element, cancellationToken);

        var all = root.DescendantsAndSelf().Where(e => Matches(e, locator)).ToList();
        var matches = all.Take(maxResults).Select(e =>
        {
            var (value, states) = Describe(e);
            return new ElementMatch(registry.GetOrAdd(new ElementKey(window, e.RuntimeId)), e.ControlType, e.Name, NullIfEmpty(e.AutomationId), value, states);
        }).ToList();
        return new ElementMatches(matches, all.Count, all.Count > matches.Count);
    }

    /// <summary>
    /// Resolves the subtree root: the whole window, or the element a ref points to. An element ref alone is enough;
    /// with both, they must agree. The window is always re-validated, so a ref can't outlive the allowlist.
    /// </summary>
    private async Task<(WindowHandle Window, RawElement Root)> ResolveRootAsync(string? hwnd, string? element, CancellationToken cancellationToken)
    {
        ElementKey? key = element is null ? null : registry.Resolve(element);
        if (hwnd is null && key is null)
            throw Invalid("Give 'hwnd' (from list_windows) or 'element' (a ref from get_ui_tree/find_elements).");

        var window = windows.ResolveTopLevel(hwnd ?? key!.Value.Window.ToString()).Window.Hwnd;
        if (key is { } k && k.Window != window)
            throw Invalid($"Element '{element}' belongs to window {k.Window}, not {window}.");

        var tree = TreeNormalizer.Normalize(await FetchAsync(window, cancellationToken));
        if (key is null)
            return (window, tree);

        var root = tree.DescendantsAndSelf().FirstOrDefault(e => e.RuntimeId == key.Value.RuntimeId)
            ?? throw new WinMcpException(new WinMcpError(
                WinMcpErrorCode.ElementStale,
                $"Element '{element}' no longer exists in window {window}.",
                Hint: "The UI changed. Call get_ui_tree or find_elements again for current refs."));
        return (window, root);
    }

    /// <summary>
    /// A timeout from a window Windows still considers responsive almost always means the tree is too large to fetch
    /// in time (~2.5 ms per element for WinForms, measured in M4), not a hang — say so rather than mislead.
    /// </summary>
    private async Task<RawElement> FetchAsync(WindowHandle window, CancellationToken cancellationToken)
    {
        try
        {
            return await automation.GetWindowTreeAsync(window, cancellationToken);
        }
        catch (WinMcpException ex) when (ex.Error.Code == WinMcpErrorCode.TargetNotResponding && IsResponding(window))
        {
            throw new WinMcpException(ex.Error with
            {
                Message = $"Fetching the UI tree of {window} timed out although the application is responding.",
                Hint = "The window's UI tree is probably too large to fetch within the time limit. "
                       + "This is a known WinMCP limitation for very large windows (e.g. lists with thousands of rows).",
            }, ex);
        }
    }

    private bool IsResponding(WindowHandle window)
    {
        try { return windows.ResolveTopLevel(window.ToString()).Responding; }
        catch (WinMcpException) { return false; } // closed meanwhile: keep the original error
    }

    private static bool Matches(RawElement e, ElementLocator locator) =>
        (locator.AutomationId is null || string.Equals(e.AutomationId, locator.AutomationId, StringComparison.OrdinalIgnoreCase))
        && (locator.Name is null || string.Equals(e.Name, locator.Name, StringComparison.OrdinalIgnoreCase))
        && (locator.NameContains is null || e.Name.Contains(locator.NameContains, StringComparison.OrdinalIgnoreCase))
        && (locator.ControlType is null || string.Equals(e.ControlType, locator.ControlType, StringComparison.OrdinalIgnoreCase))
        && (locator.ClassName is null || string.Equals(e.ClassName, locator.ClassName, StringComparison.OrdinalIgnoreCase));

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

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    private static void RequireRange(int value, int min, int max, string name)
    {
        if (value < min || value > max)
            throw Invalid($"{name} must be between {min} and {max}, got {value}.");
    }

    private static WinMcpException Invalid(string message, string? hint = null) =>
        new(new WinMcpError(WinMcpErrorCode.InvalidArgument, message, hint));
}
