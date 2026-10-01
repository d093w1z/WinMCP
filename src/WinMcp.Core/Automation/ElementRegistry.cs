using WinMcp.Core.Errors;

namespace WinMcp.Core.Automation;

/// <summary>
/// Issues short, session-scoped element references (<c>e1</c>, <c>e2</c>, ...). The same element always gets the
/// same reference for the lifetime of the server, so refs from different calls can be compared and reused.
/// </summary>
public sealed class ElementRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<ElementKey, string> _refs = [];
    private readonly Dictionary<string, ElementKey> _keys = new(StringComparer.OrdinalIgnoreCase);

    public string GetOrAdd(ElementKey key)
    {
        lock (_lock)
        {
            if (_refs.TryGetValue(key, out var existing))
                return existing;
            var reference = $"e{_refs.Count + 1}";
            _refs[key] = reference;
            _keys[reference] = key;
            return reference;
        }
    }

    public ElementKey Resolve(string reference)
    {
        lock (_lock)
        {
            if (_keys.TryGetValue(reference.Trim(), out var key))
                return key;
        }
        throw new WinMcpException(new WinMcpError(
            WinMcpErrorCode.ElementNotFound,
            $"Unknown element reference '{reference}'.",
            Hint: "Use a 'ref' returned by get_ui_tree or find_elements in this session."));
    }
}
