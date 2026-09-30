using System.Text;
using WinMcp.Core.Native;

namespace WinMcp.Testing;

/// <summary>In-memory <see cref="INativeProcesses"/>: modules per process, dialog resources per file.</summary>
internal sealed class FakeNativeProcesses : INativeProcesses
{
    public Dictionary<int, List<LoadedModule>> Modules { get; } = [];

    public Dictionary<string, List<DialogResource>> Dialogs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> Satellites { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Executables that contain the static-MFC marker strings.</summary>
    public HashSet<string> StaticMfcExecutables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int ModuleCalls { get; private set; }

    public int MarkerScans { get; private set; }

    public IReadOnlyList<LoadedModule>? GetModules(int pid)
    {
        ModuleCalls++;
        return Modules.TryGetValue(pid, out var modules) ? modules : null;
    }

    public string? GetFileVersion(string path) => path.Contains("mfc", StringComparison.OrdinalIgnoreCase) ? "14.44.35207.1" : null;

    public IReadOnlyList<DialogResource> GetDialogResources(string modulePath) =>
        Dialogs.TryGetValue(modulePath, out var dialogs) ? dialogs : [];

    public IReadOnlyList<string> GetSatelliteModules(string exePath) =>
        Satellites.TryGetValue(exePath, out var files) ? files : [];

    public bool FileContainsAsciiStrings(string path, IReadOnlyList<string> markers)
    {
        MarkerScans++;
        return StaticMfcExecutables.Contains(path);
    }

    public static LoadedModule Module(string path) => new(Path.GetFileName(path), path);
}

/// <summary>Writes dialog templates in the resource compiler's binary layout (DLGTEMPLATE / DLGTEMPLATEEX).</summary>
internal static class DialogTemplateBuilder
{
    private const uint DsSetFont = 0x40;

    /// <param name="items">Class: a <see cref="ushort"/> ordinal (0x80 Button, 0x81 Edit, 0x82 Static, 0x85 ComboBox) or a class name.</param>
    public static byte[] Extended(string caption, params (int Id, object Class, string Text)[] items)
    {
        var w = new Writer();
        w.U16(1); w.U16(0xFFFF); // dlgVer, signature
        w.U32(0); w.U32(0); // helpID, exStyle
        w.U32(0x80C800C0 | DsSetFont); // WS_POPUP | WS_CAPTION | WS_SYSMENU | DS_MODALFRAME | DS_SETFONT
        w.U16((ushort)items.Length);
        w.U16(0); w.U16(0); w.U16(200); w.U16(100);
        w.U16(0); // no menu
        w.U16(0); // default class
        w.Str(caption);
        w.U16(9); w.U16(400); w.U8(0); w.U8(1); w.Str("Segoe UI");
        foreach (var (id, cls, text) in items)
        {
            w.Align4();
            w.U32(0); w.U32(0); w.U32(0x50010000); // helpID, exStyle, WS_CHILD | WS_VISIBLE | WS_TABSTOP
            w.U16(1); w.U16(2); w.U16(50); w.U16(14);
            w.U32(unchecked((uint)id));
            w.SzOrOrd(cls);
            w.Str(text);
            w.U16(2); w.U8(0xAB); w.U8(0xCD); // creation data, skipped by the parser
        }
        return w.ToArray();
    }

    public static byte[] Classic(string caption, params (int Id, object Class, string Text)[] items)
    {
        var w = new Writer();
        w.U32(0x80C800C0 | DsSetFont);
        w.U32(0);
        w.U16((ushort)items.Length);
        w.U16(0); w.U16(0); w.U16(200); w.U16(100);
        w.U16(0);
        w.U16(0);
        w.Str(caption);
        w.U16(8); w.Str("MS Shell Dlg");
        foreach (var (id, cls, text) in items)
        {
            w.Align4();
            w.U32(0x50010000); w.U32(0);
            w.U16(1); w.U16(2); w.U16(50); w.U16(14);
            w.U16(unchecked((ushort)id));
            w.SzOrOrd(cls);
            w.Str(text);
            w.U16(0);
        }
        return w.ToArray();
    }

    private sealed class Writer
    {
        private readonly MemoryStream _stream = new();

        public void U8(byte value) => _stream.WriteByte(value);

        public void U16(ushort value) => _stream.Write(BitConverter.GetBytes(value));

        public void U32(uint value) => _stream.Write(BitConverter.GetBytes(value));

        public void Str(string value)
        {
            _stream.Write(Encoding.Unicode.GetBytes(value));
            U16(0);
        }

        public void SzOrOrd(object value)
        {
            if (value is ushort ordinal)
            {
                U16(0xFFFF);
                U16(ordinal);
            }
            else
            {
                Str((string)value);
            }
        }

        public void Align4()
        {
            while (_stream.Length % 4 != 0)
                _stream.WriteByte(0);
        }

        public byte[] ToArray() => _stream.ToArray();
    }
}
