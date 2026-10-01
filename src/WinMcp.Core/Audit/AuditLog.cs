using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMcp.Core.Audit;

/// <summary>One control action, successful or not. The primary evidence for "was it the agent or WinMCP?" triage.</summary>
/// <param name="Target">What the action was aimed at (control type, name, automation id), when it was resolved.</param>
/// <param name="Outcome"><c>ok</c> or the error code.</param>
/// <param name="Method">Mechanism used (<c>uia.*</c>/<c>win32.*</c>); null when the action failed before acting.</param>
public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string Tool,
    string? Window,
    string? Process,
    string? Element,
    string? Target,
    IReadOnlyDictionary<string, object?> Arguments,
    string Outcome,
    string? Method,
    long ElapsedMs);

public interface IAuditLog
{
    void Record(AuditEntry entry);
}

/// <summary>Appends one JSON object per line to <c>audit-YYYYMMDD.jsonl</c> in the configured directory.</summary>
public sealed class JsonlAuditLog(string directory) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Lock _lock = new();

    public string Directory { get; } = directory;

    public void Record(AuditEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, Json);
        lock (_lock)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.AppendAllText(Path.Combine(Directory, $"audit-{entry.Timestamp:yyyyMMdd}.jsonl"), line + Environment.NewLine);
        }
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMCP", "audit");
}
