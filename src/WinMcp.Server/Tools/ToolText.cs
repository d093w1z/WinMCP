namespace WinMcp.Server.Tools;

/// <summary>Parameter descriptions shared by several tools.</summary>
internal static class ToolText
{
    public const string ControlSymbol =
        "Optional symbolic control ID of a Win32/MFC control. MFC's standard IDs (e.g. 'AFX_IDW_STATUS_BAR', 'ID_APP_EXIT') "
        + "always work for MFC applications; the application's own names (e.g. 'IDC_EDIT_NAME') need a resource.h configured "
        + "with --symbols. The numeric automation_id works either way.";
}
