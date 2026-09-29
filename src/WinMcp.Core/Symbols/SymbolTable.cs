namespace WinMcp.Core.Symbols;

/// <summary>Result of mapping a control ID to <c>resource.h</c> names.</summary>
/// <param name="Symbol">The single matching name; null when there are none or several.</param>
/// <param name="Candidates">All matching names when more than one matches (never guessed between).</param>
public sealed record SymbolMatch(string? Symbol, IReadOnlyList<string>? Candidates)
{
    public static readonly SymbolMatch None = new(null, null);
}

/// <summary>
/// Maps control IDs to symbolic names. Resource IDs share one number space across kinds (an <c>IDD_</c> dialog and
/// an <c>IDC_</c> control are routinely both 1001), so lookups for controls consider only control-like names.
/// </summary>
public sealed class SymbolTable
{
    /// <summary>Windows standard dialog control IDs (WinUser.h), always known.</summary>
    private static readonly ResourceSymbol[] StandardControlIds =
    [
        new("IDOK", 1), new("IDCANCEL", 2), new("IDABORT", 3), new("IDRETRY", 4), new("IDIGNORE", 5),
        new("IDYES", 6), new("IDNO", 7), new("IDCLOSE", 8), new("IDHELP", 9), new("IDTRYAGAIN", 10),
        new("IDCONTINUE", 11), new("IDC_STATIC", -1), new("IDC_STATIC", 0xFFFF), // 16-bit -1 as returned by GetDlgCtrlID
    ];

    /// <summary>Prefixes of non-control resources: dialogs, commands, strings, menus/accelerators, bitmaps, icons, prompts.</summary>
    private static readonly string[] NonControlPrefixes = ["IDD_", "ID_", "IDM_", "IDS_", "IDR_", "IDB_", "IDI_", "IDP_", "IDA_"];

    private readonly ILookup<int, string> _controlNamesById;
    private readonly Dictionary<string, int> _idByControlName;

    public SymbolTable(IEnumerable<ResourceSymbol> symbols)
    {
        var all = symbols.ToList();
        // Explicit IDC_ names win; names with an unknown prefix are used only for IDs that have no IDC_ name.
        var controls = all.Where(s => s.Name.StartsWith("IDC_", StringComparison.Ordinal)).ToList();
        var idsWithControlNames = controls.Select(s => s.Value).ToHashSet();
        var unknown = all.Where(s => !s.Name.StartsWith("IDC_", StringComparison.Ordinal)
                                     && !NonControlPrefixes.Any(p => s.Name.StartsWith(p, StringComparison.Ordinal))
                                     && !idsWithControlNames.Contains(s.Value));
        var controlLike = controls.Concat(unknown).Concat(StandardControlIds)
            .DistinctBy(s => (s.Name, s.Value))
            .ToList();

        _controlNamesById = controlLike.ToLookup(s => s.Value, s => s.Name);
        _idByControlName = controlLike
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(s => s.Value).Distinct().Count() == 1 || g.Key == "IDC_STATIC")
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
    }

    public SymbolMatch LookupControl(int controlId)
    {
        var names = _controlNamesById[controlId].Distinct().Order(StringComparer.Ordinal).ToList();
        return names.Count switch
        {
            0 => SymbolMatch.None,
            1 => new SymbolMatch(names[0], null),
            _ => new SymbolMatch(null, names),
        };
    }

    public int? ControlId(string symbol) => _idByControlName.TryGetValue(symbol.Trim(), out var id) ? id : null;
}
