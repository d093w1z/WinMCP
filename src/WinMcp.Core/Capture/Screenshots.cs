using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;

namespace WinMcp.Core.Capture;

/// <param name="Png">Encoded image.</param>
/// <param name="SourceBounds">Captured screen area in physical pixels.</param>
/// <param name="Scale">Image pixels per screen pixel (≤ 1; below 1 when downscaled to fit max_edge).</param>
/// <param name="Method">How it was captured, e.g. <c>PrintWindow</c>.</param>
public sealed record CapturedImage(byte[] Png, int Width, int Height, Rect SourceBounds, double Scale, string Method);

public interface IScreenCapture
{
    /// <summary>
    /// Renders the window itself (not the screen, so overlapping windows never appear) and crops to
    /// <paramref name="region"/> (screen coordinates, inside the window). Throws TARGET_NOT_RESPONDING for hung windows.
    /// </summary>
    Task<CapturedImage> CaptureAsync(WindowHandle window, Rect region, int maxEdge, CancellationToken cancellationToken);
}

/// <summary>Agent-facing metadata that accompanies the image.</summary>
public sealed record ScreenshotInfo(
    WindowHandle Window,
    string? Element,
    Rect SourceBounds,
    int ImageWidth,
    int ImageHeight,
    double Scale,
    int Dpi,
    string Method);

public sealed record Screenshot(byte[] Png, ScreenshotInfo Info);

/// <summary>Backs <c>capture_screenshot</c>: allowlist gate, window/element region, minimized/offscreen checks.</summary>
public sealed class ScreenshotService(WindowQuery windows, UiTreeService tree, IScreenCapture capture)
{
    /// <summary>Default long edge: legible for a model, cheap in tokens. Callers can ask for more.</summary>
    public const int DefaultMaxEdge = 1280;

    public async Task<Screenshot> CaptureAsync(
        string? hwnd, string? element, ElementLocator locator, int padding, int maxEdge, CancellationToken cancellationToken)
    {
        if (maxEdge is < 64 or > 4096)
            throw Invalid($"max_edge must be between 64 and 4096, got {maxEdge}.");
        if (padding is < 0 or > 200)
            throw Invalid($"padding must be between 0 and 200, got {padding}.");

        WindowInfo window;
        Rect region;
        string? reference = null;
        if (element is null && locator.IsEmpty)
        {
            window = windows.ResolveTopLevel(hwnd).Window;
            region = window.Bounds;
        }
        else
        {
            var target = await tree.ResolveElementAsync(hwnd, element, locator, cancellationToken);
            window = target.Window;
            reference = target.Ref;
            var bounds = target.Element.Bounds;
            if (target.Element.IsOffscreen || bounds.Width <= 0 || bounds.Height <= 0)
                throw new WinMcpException(new WinMcpError(WinMcpErrorCode.ElementOffscreen,
                    $"Element '{reference}' is not visible on screen, so it can't be captured.",
                    Hint: "It may be scrolled out of view or on a hidden tab. Capture the window instead, or make the element visible first."));
            region = Intersect(Inflate(bounds, padding), window.Bounds)
                ?? throw new WinMcpException(new WinMcpError(WinMcpErrorCode.ElementOffscreen,
                    $"Element '{reference}' lies outside its window's visible area."));
        }

        if (window.Minimized)
            throw new WinMcpException(new WinMcpError(WinMcpErrorCode.WindowMinimized,
                $"Window {window.Hwnd} is minimized, so there is nothing to capture.",
                Hint: "Minimized windows have no rendered content. Ask the user to restore it."));

        var image = await capture.CaptureAsync(window.Hwnd, region, maxEdge, cancellationToken);
        return new Screenshot(image.Png, new ScreenshotInfo(
            window.Hwnd, reference, image.SourceBounds, image.Width, image.Height, image.Scale, window.Dpi, image.Method));
    }

    private static Rect Inflate(Rect r, int by) => new(r.X - by, r.Y - by, r.Width + 2 * by, r.Height + 2 * by);

    private static Rect? Intersect(Rect a, Rect b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : null;
    }

    private static WinMcpException Invalid(string message) => new(new WinMcpError(WinMcpErrorCode.InvalidArgument, message));
}
