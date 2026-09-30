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

    /// <summary>Prefixes of command IDs (menu items, toolbar buttons).</summary>
    private static readonly string[] CommandPrefixes = ["ID_", "IDM_"];

    private readonly ILookup<int, string> _controlNamesById;
    private readonly ILookup<int, string> _commandNamesById;
    private readonly ILookup<int, string> _dialogNamesById;
    private readonly Dictionary<string, int> _idByControlName;

    /// <param name="mfcStandardIds">
    /// Also know MFC's standard window and command IDs (<see cref="Mfc.AfxIds"/>), for MFC processes. The application's
    /// own names win for any ID they define.
    /// </param>
    public SymbolTable(IEnumerable<ResourceSymbol> symbols, bool mfcStandardIds = false)
    {
        var all = symbols.ToList();
        var commands = all.Where(s => CommandPrefixes.Any(p => s.Name.StartsWith(p, StringComparison.Ordinal))).ToList();
        if (mfcStandardIds)
            commands = WithFallback(commands, Mfc.AfxIds.Commands);
        _commandNamesById = commands.ToLookup(s => s.Value, s => s.Name);
        _dialogNamesById = all.Where(s => s.Name.StartsWith("IDD_", StringComparison.Ordinal)).ToLookup(s => s.Value, s => s.Name);
        // Explicit IDC_ names win; names with an unknown prefix are used only for IDs that have no IDC_ name.
        var controls = all.Where(s => s.Name.StartsWith("IDC_", StringComparison.Ordinal)).ToList();
        var idsWithControlNames = controls.Select(s => s.Value).ToHashSet();
        var unknown = all.Where(s => !s.Name.StartsWith("IDC_", StringComparison.Ordinal)
                                     && !NonControlPrefixes.Any(p => s.Name.StartsWith(p, StringComparison.Ordinal))
                                     && !idsWithControlNames.Contains(s.Value));
        var controlLike = controls.Concat(unknown).Concat(StandardControlIds)
            .DistinctBy(s => (s.Name, s.Value))
            .ToList();
        if (mfcStandardIds)
            controlLike = WithFallback(controlLike, Mfc.AfxIds.Windows);

        _controlNamesById = controlLike.ToLookup(s => s.Value, s => s.Name);
        // Locators may name controls and commands (menu items carry their command ID as automation id, M10).
        _idByControlName = controlLike.Concat(commands)
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(s => s.Value).Distinct().Count() == 1 || g.Key == "IDC_STATIC")
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A table with no application symbols: only the standard IDs.</summary>
    public static SymbolTable Standard(bool mfcStandardIds) => new([], mfcStandardIds);

    public SymbolMatch LookupControl(int controlId) => Match(_controlNamesById[controlId]);

    /// <summary>Command IDs: <c>ID_*</c>/<c>IDM_*</c> names (menu items, toolbar buttons).</summary>
    public SymbolMatch LookupCommand(int commandId) => Match(_commandNamesById[commandId]);

    /// <summary>Dialog template IDs: <c>IDD_*</c> names.</summary>
    public SymbolMatch LookupDialog(int dialogId) => Match(_dialogNamesById[dialogId]);

    public int? ControlId(string symbol) => _idByControlName.TryGetValue(symbol.Trim(), out var id) ? id : null;

    private static SymbolMatch Match(IEnumerable<string> matches)
    {
        var names = matches.Distinct().Order(StringComparer.Ordinal).ToList();
        return names.Count switch
        {
            0 => SymbolMatch.None,
            1 => new SymbolMatch(names[0], null),
            _ => new SymbolMatch(null, names),
        };
    }

    /// <summary>Adds the fallback names for IDs the application doesn't name itself.</summary>
    private static List<ResourceSymbol> WithFallback(List<ResourceSymbol> own, IEnumerable<(string Name, int Id)> fallback)
    {
        var named = own.Select(s => s.Value).ToHashSet();
        return own.Concat(fallback.Where(f => !named.Contains(f.Id)).Select(f => new ResourceSymbol(f.Name, f.Id))).ToList();
    }
}
