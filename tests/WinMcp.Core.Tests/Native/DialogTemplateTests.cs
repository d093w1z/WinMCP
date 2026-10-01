using WinMcp.Core.Native;
using WinMcp.Testing;

namespace WinMcp.Core.Tests.Native;

public sealed class DialogTemplateParserTests
{
    private const ushort Button = 0x80, Edit = 0x81, Static = 0x82, ComboBox = 0x85;

    [Fact]
    public void Parses_an_extended_template_with_ordinal_and_named_classes()
    {
        var data = DialogTemplateBuilder.Extended("Settings",
            (-1, Static, "Name:"), (1000, Edit, ""), (1001, ComboBox, ""), (1012, "SysListView32", "List1"), (1, Button, "OK"));

        var template = DialogTemplateParser.Parse(new DialogResource("app.exe", 102, null, data))!;

        Assert.Equal(("app.exe", 102, "Settings"), (template.Module, template.Id, template.Caption));
        Assert.Equal(
            [(-1, "Static", "Name:"), (1000, "Edit", ""), (1001, "ComboBox", ""), (1012, "SysListView32", "List1"), (1, "Button", "OK")],
            template.Items.Select(i => (i.Id, i.ClassName, i.Text)));
    }

    [Fact]
    public void Parses_a_classic_template_and_reads_16_bit_static_ids_as_minus_one()
    {
        var data = DialogTemplateBuilder.Classic("About", (0xFFFF, Static, "Version 1.0"), (1, Button, "OK"));

        var template = DialogTemplateParser.Parse(new DialogResource("app.exe", null, "ABOUTBOX", data))!;

        Assert.Equal(("ABOUTBOX", "About"), (template.Name, template.Caption));
        Assert.Equal([(-1, "Static"), (1, "Button")], template.Items.Select(i => (i.Id, i.ClassName)));
    }

    [Fact]
    public void Truncated_resource_yields_null_rather_than_throwing()
    {
        var data = DialogTemplateBuilder.Extended("Settings", (1000, Edit, ""), (1001, Edit, ""));

        Assert.Null(DialogTemplateParser.Parse(new DialogResource("app.exe", 1, null, data[..(data.Length - 20)])));
        Assert.Null(DialogTemplateParser.Parse(new DialogResource("app.exe", 1, null, [1, 0])));
    }
}

public sealed class DialogMatcherTests
{
    private static DialogTemplate Template(int id, string caption, params int[] controlIds) =>
        new("app.exe", id, null, caption, 0, controlIds.Select(c => new DialogItem(c, "Edit", "", 0)).ToList());

    [Fact]
    public void Identical_control_ids_match_fully_and_statics_are_ignored()
    {
        var match = DialogMatcher.Best([1000, 1001, 1, -1, 0xFFFF, 0], "Settings", [Template(102, "Settings", 1000, 1001, 1, -1)])!;

        Assert.Equal((102, 1.0), (match.Template.Id, match.Score));
        Assert.Empty(match.Missing);
        Assert.Empty(match.Extra);
    }

    [Fact]
    public void Reports_controls_created_at_run_time_and_missing_ones()
    {
        var match = DialogMatcher.Best([1000, 1001, 1002, 1101], "Main", [Template(100, "Main", 1000, 1001, 1002, 1003)])!;

        Assert.Equal([1101], match.Extra);
        Assert.Equal([1003], match.Missing);
        Assert.Equal(0.6, match.Score); // 3 shared of 5
    }

    [Fact]
    public void Picks_the_best_of_several_templates()
    {
        var match = DialogMatcher.Best([1000, 1001, 1002], "Main", [Template(100, "Main", 1000, 1001, 1002), Template(101, "Other", 1000, 1001, 1500, 1501)])!;

        Assert.Equal(100, match.Template.Id);
        Assert.Null(match.Alternatives.FirstOrDefault());
    }

    [Fact]
    public void A_single_shared_id_needs_the_caption_to_agree()
    {
        Assert.Null(DialogMatcher.Best([1], "About", [Template(200, "Something else", 1)]));
        Assert.Equal(200, DialogMatcher.Best([1], "About", [Template(200, "About", 1)])!.Template.Id);
    }

    [Fact]
    public void Weak_overlap_is_no_match()
    {
        Assert.Null(DialogMatcher.Best([1000, 1001, 1002, 1003], "X", [Template(100, "X", 1000, 2000, 2001, 2002)]));
        Assert.Null(DialogMatcher.Best([-1, 0], "X", [Template(100, "X", -1)]));
    }

    [Fact]
    public void Equally_good_templates_are_reported_as_alternatives()
    {
        var match = DialogMatcher.Best([1000, 1001], "Main", [Template(100, "Main", 1000, 1001), Template(101, "Main", 1000, 1001)])!;

        Assert.Equal(100, match.Template.Id);
        Assert.Equal([101], match.Alternatives.Select(a => a.Id));
    }
}
