namespace WinMcp.TestApp;

internal static class Program
{
    /// <summary>
    /// Usage: <c>WinMcp.TestApp.exe [--position x,y]</c>. A fixed position keeps screenshots and bounds stable in tests.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        Point? position = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--position" && i + 1 < args.Length && TryParsePoint(args[++i], out var p))
                position = p;
            else
                return Fail($"Unrecognized argument '{args[i]}'. Usage: WinMcp.TestApp.exe [--position x,y]");
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(position));
        return 0;
    }

    private static bool TryParsePoint(string value, out Point point)
    {
        var parts = value.Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
        {
            point = new Point(x, y);
            return true;
        }
        point = default;
        return false;
    }

    private static int Fail(string message)
    {
        MessageBox.Show(message, "WinMCP Test App", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 2;
    }
}
