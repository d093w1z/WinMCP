using System.Text;
using WinMcp.Core.Policy;
using WinMcp.Core.Symbols;

namespace WinMcp.Core.Tests.Symbols;

public sealed class ResourceSymbolParserTests
{
    /// <summary>Shape of a Visual Studio-generated resource.h, plus common hand edits.</summary>
    public const string VisualStudioHeader = """
        //{{NO_DEPENDENCIES}}
        // Microsoft Visual C++ generated include file.
        // Used by MfcTestApp.rc
        //
        #define IDD_MFCTESTAPP_DIALOG           102
        #define IDR_MAINFRAME                   128
        #define IDC_EDIT_NAME                   1000
        #define IDC_COMBO_TYPE                  1001
        #define IDC_CHECK_ENABLE                1002
        #define IDC_BUTTON_APPLY                0x3EB
        #define IDC_STATUS              (1004)  // hand-edited, parenthesized
        #define ID_FILE_EXPORT                  32771
        #define IDS_APP_TITLE                   103
        #define   IDC_SPACED   1005 /* odd spacing and comment */
        #define NOT_A_NUMBER                    IDC_EDIT_NAME
        #define WIDE_TEXT                       L"text"

        // Next default values for new objects
        //
        #ifdef APSTUDIO_INVOKED
        #ifndef APSTUDIO_READONLY_SYMBOLS
        #define _APS_NEXT_RESOURCE_VALUE        130
        #define _APS_NEXT_COMMAND_VALUE         32772
        #define _APS_NEXT_CONTROL_VALUE         1006
        #define _APS_NEXT_SYMED_VALUE           101
        #endif
        #endif
        """;

    [Fact]
    public void Parses_decimal_hex_and_parenthesized_defines_and_skips_bookkeeping()
    {
        var symbols = ResourceSymbolParser.Parse(VisualStudioHeader).ToDictionary(s => s.Name, s => s.Value);

        Assert.Equal(1000, symbols["IDC_EDIT_NAME"]);
        Assert.Equal(1003, symbols["IDC_BUTTON_APPLY"]);
        Assert.Equal(1004, symbols["IDC_STATUS"]);
        Assert.Equal(1005, symbols["IDC_SPACED"]);
        Assert.Equal(102, symbols["IDD_MFCTESTAPP_DIALOG"]);
        Assert.DoesNotContain(symbols.Keys, k => k.StartsWith("_APS_", StringComparison.Ordinal));
        Assert.DoesNotContain("NOT_A_NUMBER", symbols.Keys);
        Assert.DoesNotContain("WIDE_TEXT", symbols.Keys);
    }
}

public sealed class SymbolTableTests
{
    private static SymbolTable Table(string header) => new(ResourceSymbolParser.Parse(header));

    [Fact]
    public void Control_lookup_ignores_other_resource_kinds_sharing_the_number()
    {
        var table = Table("""
            #define IDD_MAIN      1001
            #define IDS_TITLE     1001
            #define ID_FILE_OPEN  1001
            #define IDC_EDIT_NAME 1001
            """);

        Assert.Equal(new SymbolMatch("IDC_EDIT_NAME", null), table.LookupControl(1001));
    }

    [Fact]
    public void Several_control_names_for_one_id_are_reported_as_candidates_not_guessed()
    {
        var table = Table("""
            #define IDC_OLD_NAME 1001
            #define IDC_NEW_NAME 1001
            """);

        var match = table.LookupControl(1001);

        Assert.Null(match.Symbol);
        Assert.Equal(["IDC_NEW_NAME", "IDC_OLD_NAME"], match.Candidates);
    }

    [Theory]
    [InlineData(1, "IDOK")]
    [InlineData(2, "IDCANCEL")]
    [InlineData(-1, "IDC_STATIC")]
    [InlineData(0xFFFF, "IDC_STATIC")]
    public void Standard_ids_are_always_known(int id, string expected) =>
        Assert.Equal(expected, Table("").LookupControl(id).Symbol);

    [Fact]
    public void Unprefixed_names_are_used_only_when_no_IDC_name_exists()
    {
        var table = Table("""
            #define MY_CUSTOM_CONTROL 2000
            #define LEGACY_NAME       2001
            #define IDC_MODERN        2001
            """);

        Assert.Equal("MY_CUSTOM_CONTROL", table.LookupControl(2000).Symbol);
        Assert.Equal("IDC_MODERN", table.LookupControl(2001).Symbol);
    }

    [Fact]
    public void Unknown_id_has_no_symbol() =>
        Assert.Equal(SymbolMatch.None, Table("#define IDC_A 1").LookupControl(4242));

    [Fact]
    public void Resolves_names_to_ids_case_insensitively()
    {
        var table = Table(ResourceSymbolParserTests.VisualStudioHeader);

        Assert.Equal(1001, table.ControlId("idc_combo_type"));
        Assert.Equal(1, table.ControlId("IDOK"));
        Assert.Null(table.ControlId("IDD_MFCTESTAPP_DIALOG")); // not a control
        Assert.Null(table.ControlId("IDC_MISSING"));
    }

    [Fact]
    public void Commands_and_dialogs_have_their_own_lookups()
    {
        var table = Table(ResourceSymbolParserTests.VisualStudioHeader);

        Assert.Equal("ID_FILE_EXPORT", table.LookupCommand(32771).Symbol);
        Assert.Equal("IDD_MFCTESTAPP_DIALOG", table.LookupDialog(102).Symbol);
        Assert.Equal(SymbolMatch.None, table.LookupControl(32771));
        Assert.Equal(32771, table.ControlId("ID_FILE_EXPORT")); // menu items carry command IDs as automation id
    }

    [Fact]
    public void MFC_standard_ids_are_known_only_for_MFC_and_the_applications_names_win()
    {
        var header = """
            #define ID_MY_EXIT      0xE141
            #define IDC_EDIT_NAME   1000
            """;

        var plain = new SymbolTable(ResourceSymbolParser.Parse(header));
        var mfc = new SymbolTable(ResourceSymbolParser.Parse(header), mfcStandardIds: true);

        Assert.Equal(SymbolMatch.None, plain.LookupControl(0xE801));
        Assert.Equal("AFX_IDW_STATUS_BAR", mfc.LookupControl(0xE801).Symbol);
        Assert.Equal("AFX_IDW_PANE_FIRST", mfc.LookupControl(0xE900).Symbol);
        Assert.Equal("ID_FILE_NEW", mfc.LookupCommand(0xE100).Symbol);
        Assert.Equal("ID_MY_EXIT", mfc.LookupCommand(0xE141).Symbol); // not ID_APP_EXIT
        Assert.Equal(0xE800, mfc.ControlId("AFX_IDW_TOOLBAR"));
        Assert.Equal("IDC_EDIT_NAME", mfc.LookupControl(1000).Symbol);
    }

    [Fact]
    public void Standard_dialog_ids_can_be_left_out_for_controls_outside_dialogs()
    {
        var table = Table("#define IDC_EDIT_NAME 1000");

        Assert.Equal(SymbolMatch.None, table.LookupControl(1, standardDialogIds: false));
        Assert.Equal(SymbolMatch.None, table.LookupControl(-1, standardDialogIds: false));
        Assert.Equal("IDC_EDIT_NAME", table.LookupControl(1000, standardDialogIds: false).Symbol);
        Assert.Equal("IDOK", table.LookupControl(1).Symbol);
    }

    [Fact]
    public void Standard_MFC_table_has_no_application_symbols() =>
        Assert.Equal(("ID_APP_EXIT", null), (SymbolTable.Standard(mfcStandardIds: true).LookupCommand(0xE141).Symbol, SymbolTable.Standard(true).LookupControl(1000).Symbol));
}

public sealed class SymbolProviderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"winmcp-{Guid.NewGuid():N}-resource.h");

    public void Dispose() => File.Delete(_path);

    private SymbolProvider Provider() =>
        new(WinMcpOptions.Parse(["--allow", "MfcTestApp", "--symbols", $"MfcTestApp.exe={_path}"]));

    [Fact]
    public void Reads_the_file_configured_for_the_process_including_utf16()
    {
        File.WriteAllText(_path, "#define IDC_EDIT_NAME 1000", Encoding.Unicode); // VS sometimes saves resource.h as UTF-16

        var provider = Provider();

        Assert.Equal("IDC_EDIT_NAME", provider.ForProcess("mfctestapp")!.LookupControl(1000).Symbol);
        Assert.Null(provider.ForProcess("OtherApp"));
    }

    [Fact]
    public void Rereads_the_file_when_it_changes()
    {
        File.WriteAllText(_path, "#define IDC_OLD 1000");
        var provider = Provider();
        Assert.Equal("IDC_OLD", provider.ForProcess("MfcTestApp")!.LookupControl(1000).Symbol);

        File.WriteAllText(_path, "#define IDC_NEW 1000");
        File.SetLastWriteTimeUtc(_path, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal("IDC_NEW", provider.ForProcess("MfcTestApp")!.LookupControl(1000).Symbol);
    }

    [Fact]
    public void Unreadable_file_yields_no_table_instead_of_failing()
    {
        File.WriteAllText(_path, "#define IDC_A 1000");
        var provider = Provider();
        File.Delete(_path);

        Assert.Null(provider.ForProcess("MfcTestApp"));
    }
}
