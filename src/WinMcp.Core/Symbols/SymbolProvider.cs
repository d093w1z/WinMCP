using WinMcp.Core.Policy;

namespace WinMcp.Core.Symbols;

/// <summary>
/// Serves the <c>resource.h</c> symbol table configured for a process (<c>--symbols name=path</c>), re-reading the file
/// when its timestamp changes. Paths come only from operator configuration, never from tool arguments.
/// </summary>
public sealed class SymbolProvider(WinMcpOptions options)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, (DateTime Stamp, SymbolTable Table)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public bool HasSymbols(string processName) => options.Symbols.ContainsKey(processName);

    /// <returns>Null when no file is configured for the process or it can't be read (the mapping is advisory).</returns>
    public SymbolTable? ForProcess(string processName)
    {
        if (!options.Symbols.TryGetValue(processName, out var path))
            return null;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(path);
            lock (_lock)
            {
                if (_cache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                    return cached.Table;
                var table = new SymbolTable(ResourceSymbolParser.Parse(File.ReadAllText(path)));
                _cache[path] = (stamp, table);
                return table;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
