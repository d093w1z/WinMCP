using System.Text;

namespace WinMcp.Core.Automation;

/// <summary>
/// Compact, indented text form of a <see cref="UiTree"/> — several times cheaper in tokens than the JSON:
/// <code>[e3] Edit "Name:" #nameTextBox value="Mukesh" focused</code>
/// </summary>
public static class OutlineRenderer
{
    private const int MaxNameLength = 80;

    public static string Render(UiTree tree)
    {
        var builder = new StringBuilder();
        Append(builder, tree.Root, 0);
        builder.Append($"({tree.NodeCount} nodes");
        if (tree.Truncated)
            builder.Append($", truncated by {tree.TruncationReason}: pass a node's ref as 'element' to see more");
        builder.Append(')');
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, UiNode node, int depth)
    {
        builder.Append(' ', depth * 2)
            .Append('[').Append(node.Ref).Append("] ")
            .Append(node.ControlType)
            .Append(" \"").Append(Escape(node.Name, MaxNameLength)).Append('"');
        if (node.AutomationId is { } id)
            builder.Append(" #").Append(id);
        if (node.Value is { } value)
            builder.Append(" value=\"").Append(Escape(value, int.MaxValue)).Append('"');
        if (node.States is { } states)
            builder.Append(' ').AppendJoin(' ', states);
        builder.AppendLine();

        foreach (var child in node.Children ?? [])
            Append(builder, child, depth + 1);
        if (node.OmittedChildren is { } omitted)
            builder.Append(' ', (depth + 1) * 2).AppendLine($"… {omitted} more");
    }

    private static string Escape(string text, int maxLength)
    {
        var escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        return escaped.Length > maxLength ? escaped[..(maxLength - 1)] + "…" : escaped;
    }
}
