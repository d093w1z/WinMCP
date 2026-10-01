using System.Text;

namespace WinMcp.Core.Native;

/// <summary>One control of a dialog template.</summary>
/// <param name="Id">Control ID; <c>-1</c> for <c>IDC_STATIC</c>.</param>
/// <param name="ClassName">Window class: <c>Button</c>, <c>Edit</c>, … for the predefined ordinals, else the name (<c>SysListView32</c>).</param>
/// <param name="Text">Design-time caption; <c>#n</c> for a resource ordinal (e.g. a static's icon).</param>
public sealed record DialogItem(int Id, string ClassName, string Text, uint Style);

/// <summary>A parsed <c>RT_DIALOG</c> resource.</summary>
public sealed record DialogTemplate(string Module, int? Id, string? Name, string Caption, uint Style, IReadOnlyList<DialogItem> Items);

/// <summary>
/// Parses <c>DLGTEMPLATE</c> and <c>DLGTEMPLATEEX</c> resources (the layout the resource compiler emits; documented
/// on learn.microsoft.com under "DLGTEMPLATEEX structure"). Pure byte parsing: modules are read as data only.
/// </summary>
public static class DialogTemplateParser
{
    private const uint DsSetFont = 0x40;

    /// <returns>Null when the resource is malformed or truncated.</returns>
    public static DialogTemplate? Parse(DialogResource resource)
    {
        try
        {
            var reader = new Reader(resource.Data);
            var extended = resource.Data.Length >= 4 && reader.PeekU16(0) == 1 && reader.PeekU16(2) == 0xFFFF;
            return extended ? ParseExtended(resource, reader) : ParseClassic(resource, reader);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException) // BitConverter: too few bytes
        {
            return null;
        }
    }

    private static DialogTemplate ParseExtended(DialogResource resource, Reader r)
    {
        r.Skip(4 + 4 + 4); // dlgVer + signature, helpID, exStyle
        var style = r.U32();
        var count = r.U16();
        r.Skip(8); // x, y, cx, cy
        r.SzOrOrd(); // menu
        r.SzOrOrd(); // class
        var caption = r.String();
        if ((style & DsSetFont) != 0)
        {
            r.Skip(2 + 2 + 1 + 1); // point size, weight, italic, charset
            r.String(); // typeface
        }

        var items = new List<DialogItem>(count);
        for (var i = 0; i < count; i++)
        {
            r.Align4();
            r.Skip(4 + 4); // helpID, exStyle
            var itemStyle = r.U32();
            r.Skip(8);
            var id = unchecked((int)r.U32());
            var itemClass = ClassName(r.SzOrOrd());
            var text = Text(r.SzOrOrd());
            r.Skip(r.U16()); // creation data
            items.Add(new DialogItem(id, itemClass, text, itemStyle));
        }
        return new DialogTemplate(resource.Module, resource.Id, resource.Name, caption, style, items);
    }

    private static DialogTemplate ParseClassic(DialogResource resource, Reader r)
    {
        var style = r.U32();
        r.Skip(4); // exStyle
        var count = r.U16();
        r.Skip(8);
        r.SzOrOrd();
        r.SzOrOrd();
        var caption = r.String();
        if ((style & DsSetFont) != 0)
        {
            r.Skip(2);
            r.String();
        }

        var items = new List<DialogItem>(count);
        for (var i = 0; i < count; i++)
        {
            r.Align4();
            var itemStyle = r.U32();
            r.Skip(4 + 8); // exStyle, x, y, cx, cy
            var id = r.U16();
            var itemClass = ClassName(r.SzOrOrd());
            var text = Text(r.SzOrOrd());
            r.Skip(r.U16());
            items.Add(new DialogItem(id == 0xFFFF ? -1 : id, itemClass, text, itemStyle));
        }
        return new DialogTemplate(resource.Module, resource.Id, resource.Name, caption, style, items);
    }

    private static string ClassName((string? Name, int? Ordinal) value) => value switch
    {
        (_, 0x80) => "Button",
        (_, 0x81) => "Edit",
        (_, 0x82) => "Static",
        (_, 0x83) => "ListBox",
        (_, 0x84) => "ScrollBar",
        (_, 0x85) => "ComboBox",
        (_, { } other) => $"#{other}",
        ({ } name, _) => name,
        _ => "",
    };

    private static string Text((string? Name, int? Ordinal) value) => value.Name ?? (value.Ordinal is { } o ? $"#{o}" : "");

    private sealed class Reader(byte[] data)
    {
        private int _position;

        public ushort PeekU16(int at) => BitConverter.ToUInt16(data, at);

        public ushort U16()
        {
            var value = BitConverter.ToUInt16(data, _position);
            _position += 2;
            return value;
        }

        public uint U32()
        {
            var value = BitConverter.ToUInt32(data, _position);
            _position += 4;
            return value;
        }

        public void Skip(int bytes)
        {
            _position += bytes;
            if (_position > data.Length)
                throw new ArgumentOutOfRangeException(nameof(bytes), "Dialog template is truncated.");
        }

        public void Align4() => _position = (_position + 3) & ~3;

        /// <summary>A null-terminated UTF-16 string.</summary>
        public string String()
        {
            var builder = new StringBuilder();
            for (var c = U16(); c != 0; c = U16())
                builder.Append((char)c);
            return builder.ToString();
        }

        /// <summary><c>sz_Or_Ord</c>: 0x0000 = none, 0xFFFF + ordinal, or a string.</summary>
        public (string? Name, int? Ordinal) SzOrOrd()
        {
            switch (PeekU16(_position))
            {
                case 0:
                    _position += 2;
                    return (null, null);
                case 0xFFFF:
                    _position += 2;
                    return (null, U16());
                default:
                    return (String(), null);
            }
        }
    }
}

/// <summary>How well a live dialog matches a template, by control IDs.</summary>
/// <param name="Score">Jaccard similarity of the significant control-ID sets (1 = identical).</param>
/// <param name="Missing">IDs in the template without a live control (destroyed or never created).</param>
/// <param name="Extra">IDs of live controls the template doesn't have (created at run time).</param>
/// <param name="Alternatives">Other templates that match equally well; the choice is then not certain.</param>
public sealed record DialogMatch(DialogTemplate Template, double Score, IReadOnlyList<int> Missing, IReadOnlyList<int> Extra, IReadOnlyList<DialogTemplate> Alternatives);

/// <summary>
/// Finds the template a live dialog was created from. Control IDs are what a template and its live dialog share
/// reliably (captions and sizes change at run time); <c>IDC_STATIC</c> and 0 carry no identity and are ignored.
/// </summary>
public static class DialogMatcher
{
    public const double MinimumScore = 0.5;

    public static bool IsSignificant(int id) => id is not (0 or -1 or 0xFFFF);

    public static DialogMatch? Best(IReadOnlyCollection<int> liveControlIds, string liveCaption, IEnumerable<DialogTemplate> templates)
    {
        var live = liveControlIds.Where(IsSignificant).ToHashSet();
        if (live.Count == 0)
            return null;

        var scored = templates
            .Select(t =>
            {
                var ids = t.Items.Select(i => i.Id).Where(IsSignificant).ToHashSet();
                var common = ids.Count(live.Contains);
                var score = common == 0 ? 0 : (double)common / ids.Union(live).Count();
                var sameCaption = t.Caption.Length > 0 && string.Equals(t.Caption, liveCaption, StringComparison.Ordinal);
                return (Template: t, Ids: ids, Common: common, Score: score, SameCaption: sameCaption);
            })
            // One shared control (typically IDOK) identifies nothing unless the caption agrees.
            .Where(s => s.Score >= MinimumScore && (s.Common >= 2 || s.SameCaption))
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.SameCaption)
            .ToList();
        if (scored.Count == 0)
            return null;

        var best = scored[0];
        var alternatives = scored.Skip(1)
            .Where(s => Math.Abs(s.Score - best.Score) < 1e-9 && s.SameCaption == best.SameCaption)
            .Select(s => s.Template)
            .ToList();
        return new DialogMatch(
            best.Template,
            Math.Round(best.Score, 2),
            best.Ids.Where(id => !live.Contains(id)).Order().ToList(),
            live.Where(id => !best.Ids.Contains(id)).Order().ToList(),
            alternatives);
    }
}
