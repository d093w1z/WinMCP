using System.Diagnostics;
using WinMcp.Core.Audit;
using WinMcp.Core.Errors;
using WinMcp.Core.Policy;

namespace WinMcp.Core.Automation;

/// <summary>
/// Backs the control tools: resolves the target through the allowlist gate, applies the checks every action needs,
/// performs it, and audits the attempt whatever the outcome.
/// </summary>
public sealed class InteractionService(UiTreeService tree, IUiAutomation automation, WinMcpOptions options, IAuditLog audit)
{
    public async Task<ActionResult> PerformAsync(
        string? hwnd, string? element, ElementLocator locator, ElementAction action, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        UiTreeService.ResolvedElement? target = null;
        string outcome = "ok";
        string? method = null;
        try
        {
            // Control tools aren't even registered in observe mode; this is defence in depth.
            if (options.Mode != ServerMode.Control)
                throw new WinMcpException(new WinMcpError(WinMcpErrorCode.OperationNotPermitted,
                    "WinMCP is running in observe (read-only) mode.", Hint: "The user must start WinMCP with --mode control to allow interaction."));

            target = await tree.ResolveElementAsync(hwnd, element, locator, cancellationToken);

            // M0: UIA invokes disabled WinForms buttons anyway, running their handlers. Never rely on the target to refuse.
            if (!target.Element.IsEnabled)
                throw new WinMcpException(new WinMcpError(WinMcpErrorCode.ElementDisabled,
                    $"{Describe(target.Element)} is disabled.",
                    Hint: "Something else in the UI probably has to happen first (e.g. fill in a required field). Check with get_ui_tree or wait_for condition 'enabled'."));
            if (action is ElementAction.SetValue && target.Element.IsPassword)
                throw new WinMcpException(new WinMcpError(WinMcpErrorCode.PasswordField,
                    $"{Describe(target.Element)} is a password field; WinMCP does not enter passwords."));

            var result = await automation.PerformAsync(target.Key, action, cancellationToken);
            method = result.Method;
            return new ActionResult(true, action.Name, target.Ref, result.Method, result.Changed, result.ValueAfter, result.StateAfter, result.Warning, stopwatch.ElapsedMilliseconds);
        }
        catch (WinMcpException ex)
        {
            outcome = ex.Error.Code.ToWireName();
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            outcome = WinMcpErrorCode.InternalError.ToWireName();
            throw;
        }
        finally
        {
            audit.Record(new AuditEntry(
                DateTimeOffset.Now,
                action.Name,
                target?.Window.Hwnd.ToString() ?? hwnd,
                target?.Window.Process.Name,
                target?.Ref ?? element,
                target is null ? null : Describe(target.Element),
                Arguments(action, redact: target?.Element.IsPassword == true, locator),
                outcome,
                method,
                stopwatch.ElapsedMilliseconds));
        }
    }

    private static string Describe(RawElement e) =>
        e.AutomationId.Length > 0 ? $"{e.ControlType} \"{e.Name}\" #{e.AutomationId}" : $"{e.ControlType} \"{e.Name}\"";

    private static Dictionary<string, object?> Arguments(ElementAction action, bool redact, ElementLocator locator)
    {
        var arguments = new Dictionary<string, object?>();
        switch (action)
        {
            case ElementAction.SetValue set:
                arguments["value"] = redact ? "<redacted>" : set.Value;
                break;
            case ElementAction.Select select:
                arguments["option"] = select.Option;
                break;
            case ElementAction.SetToggle toggle:
                arguments["state"] = toggle.On ? "on" : "off";
                break;
        }
        if (!locator.IsEmpty)
            arguments["locator"] = locator;
        return arguments;
    }
}
