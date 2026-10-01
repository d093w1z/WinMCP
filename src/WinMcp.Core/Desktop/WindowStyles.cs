namespace WinMcp.Core.Desktop;

/// <summary>Decodes top-level window style bits into their Win32 names (values from WinUser.h).</summary>
public static class WindowStyles
{
    private const uint Caption = 0x00C00000; // WS_BORDER | WS_DLGFRAME

    // Ordered for readability: kind, state, frame, buttons, scrolling, clipping.
    private static readonly (uint Bit, string Name)[] StyleBits =
    [
        (0x80000000, "WS_POPUP"),
        (0x40000000, "WS_CHILD"),
        (0x10000000, "WS_VISIBLE"),
        (0x08000000, "WS_DISABLED"),
        (0x20000000, "WS_MINIMIZE"),
        (0x01000000, "WS_MAXIMIZE"),
        (0x00800000, "WS_BORDER"),
        (0x00400000, "WS_DLGFRAME"),
        (0x00040000, "WS_THICKFRAME"),
        (0x00080000, "WS_SYSMENU"),
        (0x00020000, "WS_MINIMIZEBOX"), // same bit as WS_GROUP on child windows
        (0x00010000, "WS_MAXIMIZEBOX"), // same bit as WS_TABSTOP on child windows
        (0x00200000, "WS_VSCROLL"),
        (0x00100000, "WS_HSCROLL"),
        (0x04000000, "WS_CLIPSIBLINGS"),
        (0x02000000, "WS_CLIPCHILDREN"),
    ];

    private static readonly (uint Bit, string Name)[] ExStyleBits =
    [
        (0x00000008, "WS_EX_TOPMOST"),
        (0x00000080, "WS_EX_TOOLWINDOW"),
        (0x00040000, "WS_EX_APPWINDOW"),
        (0x00000001, "WS_EX_DLGMODALFRAME"),
        (0x08000000, "WS_EX_NOACTIVATE"),
        (0x00080000, "WS_EX_LAYERED"),
        (0x00000020, "WS_EX_TRANSPARENT"),
        (0x02000000, "WS_EX_COMPOSITED"),
        (0x00200000, "WS_EX_NOREDIRECTIONBITMAP"),
        (0x00000040, "WS_EX_MDICHILD"),
        (0x00010000, "WS_EX_CONTROLPARENT"),
        (0x00000400, "WS_EX_CONTEXTHELP"),
        (0x00000010, "WS_EX_ACCEPTFILES"),
        (0x00000004, "WS_EX_NOPARENTNOTIFY"),
        (0x00000100, "WS_EX_WINDOWEDGE"),
        (0x00000200, "WS_EX_CLIENTEDGE"),
        (0x00020000, "WS_EX_STATICEDGE"),
        (0x00001000, "WS_EX_RIGHT"),
        (0x00002000, "WS_EX_RTLREADING"),
        (0x00004000, "WS_EX_LEFTSCROLLBAR"),
        (0x00400000, "WS_EX_LAYOUTRTL"),
        (0x00100000, "WS_EX_NOINHERITLAYOUT"),
    ];

    /// <summary>WS_CAPTION is reported instead of WS_BORDER + WS_DLGFRAME when both are set.</summary>
    public static IReadOnlyList<string> DecodeStyle(uint style)
    {
        var names = new List<string>();
        var remaining = style;
        if ((style & Caption) == Caption)
        {
            names.Add("WS_CAPTION");
            remaining &= ~Caption;
        }
        Decode(remaining, StyleBits, names);
        return names;
    }

    public static IReadOnlyList<string> DecodeExStyle(uint exStyle)
    {
        var names = new List<string>();
        Decode(exStyle, ExStyleBits, names);
        return names;
    }

    private static void Decode(uint value, (uint Bit, string Name)[] table, List<string> names)
    {
        foreach (var (bit, name) in table)
        {
            if ((value & bit) != 0)
            {
                names.Add(name);
                value &= ~bit;
            }
        }
        if (value != 0)
            names.Add($"0x{value:X8}"); // undocumented or unknown bits, reported rather than dropped
    }
}
