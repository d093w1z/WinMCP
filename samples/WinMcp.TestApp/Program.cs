namespace WinMcp.TestApp;

internal static class Program
{
    private const string Usage = "Usage: WinMcp.TestApp.exe [--position x,y] [--stress <1-2000>]";

    /// <summary>
    /// <c>--position</c> keeps screenshots and bounds stable in tests; <c>--stress N</c> adds N buttons in a
    /// scrolling panel to produce a large UI Automation tree for performance measurements.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        Point? position = null;
        var stressCount = 0;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--position" && i + 1 < args.Length && TryParsePoint(args[++i], out var p))
                position = p;
            else if (args[i] == "--stress" && i + 1 < args.Length && int.TryParse(args[++i], out var n) && n is >= 1 and <= 2000)
                stressCount = n;
            else
                return Fail($"Unrecognized argument '{args[i]}'. {Usage}");
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(position, stressCount));
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
