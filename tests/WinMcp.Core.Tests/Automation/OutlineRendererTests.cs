using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;

namespace WinMcp.Core.Tests.Automation;

public sealed class OutlineRendererTests
{
    private static UiNode Node(string reference, string type, string name, string? id = null, string? value = null, string[]? states = null, UiNode[]? children = null, int? omitted = null) =>
        new(reference, type, name, id, value, states, children, omitted);

    [Fact]
    public void Renders_indented_lines_with_ids_values_and_states()
    {
        var tree = new UiTree(new WindowHandle(0x10), Node("e1", "Window", "WinMCP Test App", "MainForm", children:
        [
            Node("e2", "Edit", "Name:", "nameTextBox", value: "Mukesh", states: ["focused"]),
            Node("e3", "Button", "Advanced...", "advancedButton", states: ["disabled"]),
        ]), NodeCount: 3, Truncated: false, TruncationReason: null);

        Assert.Equal(
            """
            [e1] Window "WinMCP Test App" #MainForm
              [e2] Edit "Name:" #nameTextBox value="Mukesh" focused
              [e3] Button "Advanced..." #advancedButton disabled
            (3 nodes)
            """.ReplaceLineEndings(),
            OutlineRenderer.Render(tree));
    }

    [Fact]
    public void Shows_omitted_children_and_truncation()
    {
        var tree = new UiTree(new WindowHandle(0x10), Node("e1", "Window", "App", omitted: 12), 1, true, "max_nodes");

        var outline = OutlineRenderer.Render(tree);

        Assert.Contains("  … 12 more", outline);
        Assert.Contains("truncated by max_nodes", outline);
    }

    [Fact]
    public void Escapes_quotes_and_newlines_and_shortens_long_names()
    {
        var tree = new UiTree(new WindowHandle(0x10), Node("e1", "Text", new string('n', 100), value: "say \"hi\"\r\nbye"), 1, false, null);

        var line = OutlineRenderer.Render(tree).Split(Environment.NewLine)[0];

        Assert.Contains("value=\"say \\\"hi\\\"\\r\\nbye\"", line);
        Assert.Contains(new string('n', 79) + "…\"", line);
    }
}
