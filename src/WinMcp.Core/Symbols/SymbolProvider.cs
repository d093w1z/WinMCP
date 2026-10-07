using WinMcp.Core.Policy;

namespace WinMcp.Core.Symbols;

/// <summary>
/// Serves the <c>resource.h</c> symbol table configured for a process (<c>--symbols name=path</c>), re-reading the file
/// when its timestamp changes. Paths come only from operator configuration, never from tool arguments.
/// </summary>
public sealed class SymbolProvider(WinMcpOptions options)
{
    private static readonly SymbolTable MfcStandard = SymbolTable.Standard(mfcStandardIds: true);
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Path, bool Mfc), (DateTime Stamp, SymbolTable Table)> _cache = [];

    public bool HasSymbols(string processName) => options.Symbols.ContainsKey(processName);

    /// <param name="mfc">The process uses MFC: its standard IDs (<c>ID_APP_EXIT</c>, <c>AFX_IDW_STATUS_BAR</c>, …) are known too, even without a configured file.</param>
    /// <returns>Null when there is nothing to map: no file configured (or readable) and not MFC. The mapping is advisory.</returns>
    public SymbolTable? ForProcess(string processName, bool mfc = false)
    {
        if (!options.Symbols.TryGetValue(processName, out var path))
            return mfc ? MfcStandard : null;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            lock (_lock)
            {
                if (_cache.TryGetValue((path, mfc), out var cached) && cached.Stamp == stamp)
                    return cached.Table;
                var table = new SymbolTable(ResourceSymbolParser.Parse(File.ReadAllText(path)), mfc);
                _cache[(path, mfc)] = (stamp, table);
                return table;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return mfc ? MfcStandard : null;
        }
    }
}
