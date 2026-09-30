using System.Diagnostics;
using WinMcp.Core.Audit;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;
using WinMcp.Core.Policy;

namespace WinMcp.Core.Automation;

/// <summary>
/// Backs the control tools: resolves the target through the allowlist gate, applies the checks every action needs,
/// performs it, and audits the attempt whatever the outcome.
/// </summary>
public sealed class InteractionService(
    UiTreeService tree, IUiAutomation automation, IKeyboard keyboard, WindowQuery windows, WinMcpOptions options, IAuditLog audit)
{
    /// <summary>
    /// Types into a window, optionally focusing an element first. Refuses password fields — the target element, or
    /// whatever has keyboard focus when only a window is given — and never sends input unless the window is foreground.
    /// </summary>
    public async Task<ActionResult> SendKeysAsync(
        string? hwnd, string? element, ElementLocator locator, KeyInput input, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        UiTreeService.ResolvedElement? target = null;
        WindowInfo? window = null;
        var outcome = "ok";
        string? method = null;
        var passwordInvolved = false;
        try
        {
            RequireControlMode();
            if (element is not null || !locator.IsEmpty)
            {
                target = await tree.ResolveElementAsync(hwnd, element, locator, cancellationToken);
                window = target.Window;
                RequireEnabled(target.Element);
                if (target.Element.IsPassword)
                {
                    passwordInvolved = true;
                    throw PasswordRefused(target.Element);
                }
                await automation.PerformAsync(target.Key, new ElementAction.Focus(), cancellationToken);
            }
            else
            {
                window = windows.ResolveTopLevel(hwnd).Window;
            }

            if (await tree.FocusedElementAsync(window.Hwnd, cancellationToken) is { IsPassword: true } focused)
            {
                passwordInvolved = true;
                throw PasswordRefused(focused);
            }

            var sent = await keyboard.SendAsync(window.Hwnd, input, cancellationToken);
            method = sent.Method;
            return new ActionResult(true, "send_keys", target?.Ref ?? window.Hwnd.ToString(), sent.Method, true, null, null, null, stopwatch.ElapsedMilliseconds);
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
            var arguments = new Dictionary<string, object?>
            {
                [input is KeyInput.Text ? "text" : "keys"] = passwordInvolved ? "<redacted>" : input switch
                {
                    KeyInput.Text t => t.Value,
                    KeyInput.Chords c => string.Join(", ", c.Sequence),
                    _ => null,
                },
            };
            if (!locator.IsEmpty)
                arguments["locator"] = locator;
            audit.Record(new AuditEntry(
                DateTimeOffset.Now, "send_keys", window?.Hwnd.ToString() ?? hwnd, window?.Process.Name, target?.Ref ?? element,
                target is null ? null : Describe(target.Element), arguments, outcome, method, stopwatch.ElapsedMilliseconds));
        }
    }

    private void RequireControlMode()
    {
        // Control tools aren't even registered in observe mode; this is defence in depth.
        if (options.Mode != ServerMode.Control)
            throw new WinMcpException(new WinMcpError(WinMcpErrorCode.OperationNotPermitted,
                "WinMCP is running in observe (read-only) mode.", Hint: "The user must start WinMCP with --mode control to allow interaction."));
    }

    // M0: UIA invokes disabled WinForms buttons anyway, running their handlers. Never rely on the target to refuse.
    private static void RequireEnabled(RawElement element)
    {
        if (!element.IsEnabled)
            throw new WinMcpException(new WinMcpError(WinMcpErrorCode.ElementDisabled,
                $"{Describe(element)} is disabled.",
                Hint: "Something else in the UI probably has to happen first (e.g. fill in a required field). Check with get_ui_tree or wait_for condition 'enabled'."));
    }

    private static WinMcpException PasswordRefused(RawElement element) =>
        new(new WinMcpError(WinMcpErrorCode.PasswordField, $"{Describe(element)} is a password field; WinMCP does not enter passwords."));
    public async Task<ActionResult> PerformAsync(
        string? hwnd, string? element, ElementLocator locator, ElementAction action, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        UiTreeService.ResolvedElement? target = null;
        string outcome = "ok";
        string? method = null;
        try
        {
            RequireControlMode();
            target = await tree.ResolveElementAsync(hwnd, element, locator, cancellationToken);
            RequireEnabled(target.Element);
            if (action is ElementAction.SetValue && target.Element.IsPassword)
                throw PasswordRefused(target.Element);

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
