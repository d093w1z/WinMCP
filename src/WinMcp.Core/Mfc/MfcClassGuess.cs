namespace WinMcp.Core.Mfc;

/// <summary>
/// The MFC class most likely wrapping a window of an MFC application, from its window class and control ID — an
/// inference, not a fact: applications derive their own classes (a <c>CMainDlg</c> is reported as <c>CDialog</c>),
/// and a plain Win32 control inside an MFC app may have no wrapper at all. The exact runtime class needs code running
/// inside the process (see docs/mfc-investigation.md).
/// </summary>
public static class MfcClassGuess
{
    private static readonly Dictionary<string, string> StandardControls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Button"] = "CButton",
        ["Edit"] = "CEdit",
        ["Static"] = "CStatic",
        ["ComboBox"] = "CComboBox",
        ["ComboBoxEx32"] = "CComboBoxEx",
        ["ListBox"] = "CListBox",
        ["ScrollBar"] = "CScrollBar",
        ["SysListView32"] = "CListCtrl",
        ["SysTreeView32"] = "CTreeCtrl",
        ["SysTabControl32"] = "CTabCtrl",
        ["SysHeader32"] = "CHeaderCtrl",
        ["msctls_progress32"] = "CProgressCtrl",
        ["msctls_trackbar32"] = "CSliderCtrl",
        ["msctls_updown32"] = "CSpinButtonCtrl",
        ["msctls_hotkey32"] = "CHotKeyCtrl",
        ["SysDateTimePick32"] = "CDateTimeCtrl",
        ["SysMonthCal32"] = "CMonthCalCtrl",
        ["SysIPAddress32"] = "CIPAddressCtrl",
        ["SysAnimate32"] = "CAnimateCtrl",
        ["SysLink"] = "CLinkCtrl",
        ["RichEdit20W"] = "CRichEditCtrl",
        ["RICHEDIT50W"] = "CRichEditCtrl",
        ["MDIClient"] = "MDI client (of CMDIFrameWnd)",
    };

    private const int ToolBarId = 0xE800, StatusBarId = 0xE801, ReBarId = 0xE804, DialogBarId = 0xE805;
    private const int DockBarFirst = 0xE81B, DockBarLast = 0xE81F, PaneFirst = 0xE900, PaneLast = 0xE9FF;

    /// <param name="controlId">GetDlgCtrlID of a child window; ignored for top-level windows.</param>
    /// <returns>Null when nothing plausible can be said.</returns>
    public static string? Guess(string className, int? controlId, bool topLevel)
    {
        var id = topLevel ? null : controlId;
        var isPane = id is >= PaneFirst and <= PaneLast;

        if (className == "#32770")
            return topLevel ? "CDialog" : isPane ? "CFormView" : id == DialogBarId ? "CDialogBar" : "CDialog (child: property page, form view or dialog bar)";
        if (className == "ToolbarWindow32")
            return id == ToolBarId ? "CToolBar" : "CToolBar or CToolBarCtrl";
        if (className == "msctls_statusbar32")
            return id == StatusBarId ? "CStatusBar" : "CStatusBar or CStatusBarCtrl";
        if (className == "ReBarWindow32")
            return id == ReBarId ? "CReBar" : "CReBar or CReBarCtrl";
        if (StandardControls.TryGetValue(className, out var control))
            return control;

        // MFC's own registered classes: AfxWnd140u, AfxFrameOrView140u, AfxMDIFrame140u, AfxControlBar140u, and
        // Afx:<instance>:<style>:… from AfxRegisterWndClass. The suffix encodes version and linkage, not the role.
        // Feature Pack controls with their own class names (observed in M11).
        if (className.StartsWith("Afx:PropList", StringComparison.Ordinal))
            return "CMFCPropertyGridCtrl";
        if (className.StartsWith("AfxMDIFrame", StringComparison.Ordinal))
            return "CMDIFrameWnd";
        if (className.StartsWith("AfxControlBar", StringComparison.Ordinal))
            return id is >= DockBarFirst and <= DockBarLast ? "CDockBar" : "CControlBar";
        if (className.StartsWith("AfxFrameOrView", StringComparison.Ordinal) || className.StartsWith("Afx:", StringComparison.Ordinal))
            return topLevel ? "CFrameWnd" : isPane ? "CView" : "CWnd";
        if (className.StartsWith("AfxWnd", StringComparison.Ordinal) || className.StartsWith("AfxOleControl", StringComparison.Ordinal))
            return isPane ? "CView" : "CWnd";
        return null;
    }
}
