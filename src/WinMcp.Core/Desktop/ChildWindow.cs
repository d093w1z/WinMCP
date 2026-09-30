namespace WinMcp.Core.Desktop;

/// <summary>A child window (Win32 control) as seen through plain Win32 calls, without UI Automation.</summary>
/// <param name="Text">Window text via WM_GETTEXT; empty for password edits (never read).</param>
/// <param name="Style">Raw GWL_STYLE (distinguishes check boxes from push buttons, password edits, ...).</param>
/// <param name="Check">BM_GETCHECK for check boxes and radio buttons (0 off, 1 on, 2 indeterminate); otherwise null.</param>
public sealed record ChildWindow(
    WindowHandle Hwnd,
    WindowHandle Parent,
    string ClassName,
    string Text,
    int ControlId,
    uint Style,
    bool Visible,
    bool Enabled,
    Rect Bounds,
    int? Check);
