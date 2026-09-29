using System.ComponentModel;
using ModelContextProtocol.Server;
using WinMcp.Core.Automation;

namespace WinMcp.Server.Tools;

/// <summary>Interaction tools. Registered only with <c>--mode control</c>, so observe-mode clients never see them.</summary>
[McpServerToolType]
public sealed class ControlTools(InteractionService interaction)
{
    private const string TargetHelp =
        " Identify the element by 'element' (a ref), or by 'hwnd' plus criteria that match exactly one element. "
        + "Disabled elements are refused.";

    [McpServerTool(Name = "invoke", Title = "Invoke (click)", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Activates an element the way a click would: presses a button, toggles a check box, selects an item, or expands/collapses."
        + TargetHelp + " The result's 'method' says how it was done (UI Automation pattern or Win32 message).")]
    public Task<ActionResult> Invoke(
        [Description("Ref from get_ui_tree/find_elements, e.g. 'e7'.")] string? element = null,
        [Description("Window handle from list_windows; use with criteria.")] string? hwnd = null,
        [Description("Exact AutomationId.")] string? automation_id = null,
        [Description("Exact element name.")] string? name = null,
        [Description("UI Automation control type, e.g. Button.")] string? control_type = null,
        [Description("resource.h name, e.g. 'IDC_BUTTON_APPLY' (requires --symbols).")] string? control_symbol = null,
        CancellationToken cancellationToken = default) =>
        interaction.PerformAsync(hwnd, element, Locator(automation_id, name, control_type, control_symbol), new ElementAction.Invoke(), cancellationToken);

    [McpServerTool(Name = "set_value", Title = "Set value", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Replaces the text of an edit field (or other element with a settable value)." + TargetHelp
        + " Password fields are refused. Returns the value read back afterwards.")]
    public Task<ActionResult> SetValue(
        [Description("The complete new value.")] string value,
        [Description("Ref from get_ui_tree/find_elements, e.g. 'e7'.")] string? element = null,
        [Description("Window handle from list_windows; use with criteria.")] string? hwnd = null,
        [Description("Exact AutomationId.")] string? automation_id = null,
        [Description("Exact element name (for edits, usually their label).")] string? name = null,
        [Description("UI Automation control type, e.g. Edit.")] string? control_type = null,
        [Description("resource.h name, e.g. 'IDC_EDIT_NAME' (requires --symbols).")] string? control_symbol = null,
        CancellationToken cancellationToken = default) =>
        interaction.PerformAsync(hwnd, element, Locator(automation_id, name, control_type, control_symbol), new ElementAction.SetValue(value), cancellationToken);

    [McpServerTool(Name = "select_option", Title = "Select option", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Selects an item by its text in a combo box (drop-down), list or tab control; the target is the container, not the item."
        + TargetHelp + " A combo box is opened only as long as needed. Unknown options return OPTION_NOT_FOUND with the available ones.")]
    public Task<ActionResult> SelectOption(
        [Description("Text of the item to select, e.g. 'HTML'.")] string option,
        [Description("Ref of the combo box/list/tab control, e.g. 'e5'.")] string? element = null,
        [Description("Window handle from list_windows; use with criteria.")] string? hwnd = null,
        [Description("Exact AutomationId of the container.")] string? automation_id = null,
        [Description("Exact name of the container (usually its label).")] string? name = null,
        [Description("UI Automation control type, e.g. ComboBox, List, Tab.")] string? control_type = null,
        [Description("resource.h name, e.g. 'IDC_COMBO_TYPE' (requires --symbols).")] string? control_symbol = null,
        CancellationToken cancellationToken = default) =>
        interaction.PerformAsync(hwnd, element, Locator(automation_id, name, control_type, control_symbol), new ElementAction.Select(option), cancellationToken);

    [McpServerTool(Name = "set_toggle", Title = "Set check box", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Sets a check box or toggle button to 'on' or 'off'. Does nothing (changed=false) if it's already in that state, so it is safe to repeat."
        + TargetHelp)]
    public Task<ActionResult> SetToggle(
        [Description("'on' or 'off'.")] string state,
        [Description("Ref from get_ui_tree/find_elements, e.g. 'e6'.")] string? element = null,
        [Description("Window handle from list_windows; use with criteria.")] string? hwnd = null,
        [Description("Exact AutomationId.")] string? automation_id = null,
        [Description("Exact element name.")] string? name = null,
        [Description("UI Automation control type, e.g. CheckBox.")] string? control_type = null,
        [Description("resource.h name (requires --symbols).")] string? control_symbol = null,
        CancellationToken cancellationToken = default) =>
        interaction.PerformAsync(hwnd, element, Locator(automation_id, name, control_type, control_symbol), new ElementAction.SetToggle(ActionArguments.ParseToggle(state)), cancellationToken);

    private static ElementLocator Locator(string? automationId, string? name, string? controlType, string? controlSymbol) =>
        new(AutomationId: automationId, Name: name, ControlType: controlType, ControlSymbol: controlSymbol);
}
