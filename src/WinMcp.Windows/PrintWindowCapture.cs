using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.Xps;
using WinMcp.Core.Capture;
using WinMcp.Core.Errors;
using Rect = WinMcp.Core.Desktop.Rect;
using WindowHandle = WinMcp.Core.Desktop.WindowHandle;

namespace WinMcp.Windows;

/// <summary>
/// Captures by asking the window to render itself (PrintWindow) rather than copying the screen: works when the window
/// is covered, and never includes pixels of other (possibly non-allowlisted) windows. There is deliberately no
/// screen-copy fallback for that reason.
/// </summary>
public sealed class PrintWindowCapture : IScreenCapture
{
    /// <summary>PrintWindow sends WM_PRINT to the target, so a busy target blocks it.</summary>
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(5);

    /// <summary>PW_RENDERFULLCONTENT: also captures DirectComposition/GPU-rendered content (Windows 8.1+).</summary>
    private const PRINT_WINDOW_FLAGS RenderFullContent = (PRINT_WINDOW_FLAGS)2;

    public async Task<CapturedImage> CaptureAsync(WindowHandle window, Rect region, int maxEdge, CancellationToken cancellationToken)
    {
        var hwnd = (HWND)(nint)window.Value;
        if (!PInvoke.IsWindow(hwnd))
            throw new WinMcpException(new WinMcpError(WinMcpErrorCode.WindowClosed, $"Window {window} closed."));
        if (PInvoke.IsHungAppWindow(hwnd))
            throw NotResponding(window);

        var render = Task.Run(() => Render(hwnd), cancellationToken);
        if (await Task.WhenAny(render, Task.Delay(RenderTimeout, cancellationToken)) != render)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw NotResponding(window); // the render thread finishes (and is discarded) whenever the app recovers
        }

        var (full, windowRect) = await render;
        using (full)
        {
            // Region is in screen coordinates; the bitmap starts at the window rectangle (incl. invisible borders).
            var crop = Rectangle.Intersect(
                new Rectangle(region.X - windowRect.X, region.Y - windowRect.Y, region.Width, region.Height),
                new Rectangle(0, 0, full.Width, full.Height));
            if (crop.Width <= 0 || crop.Height <= 0)
                throw new WinMcpException(new WinMcpError(WinMcpErrorCode.ElementOffscreen, "The requested region is outside the window."));

            var scale = Math.Min(1.0, (double)maxEdge / Math.Max(crop.Width, crop.Height));
            var width = Math.Max(1, (int)Math.Round(crop.Width * scale));
            var height = Math.Max(1, (int)Math.Round(crop.Height * scale));

            using var output = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(output))
            {
                graphics.InterpolationMode = scale < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(full, new Rectangle(0, 0, width, height), crop, GraphicsUnit.Pixel);
            }

            using var png = new MemoryStream();
            output.Save(png, ImageFormat.Png);
            var source = new Rect(windowRect.X + crop.X, windowRect.Y + crop.Y, crop.Width, crop.Height);
            return new CapturedImage(png.ToArray(), width, height, source, Math.Round(scale, 4), "PrintWindow");
        }
    }

    /// <returns>The whole window rendered at its physical size, and its window rectangle in screen coordinates.</returns>
    private static (Bitmap Bitmap, Rect WindowRect) Render(HWND hwnd)
    {
        using var dpi = DpiScope.Enter(); // physical pixels, matching DWM frame bounds and the rendered size
        if (!PInvoke.GetWindowRect(hwnd, out var rect) || rect.right <= rect.left || rect.bottom <= rect.top)
            throw new WinMcpException(new WinMcpError(WinMcpErrorCode.WindowClosed, "The window has no area to capture."));

        var bitmap = new Bitmap(rect.right - rect.left, rect.bottom - rect.top, PixelFormat.Format32bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            var hdc = graphics.GetHdc();
            try
            {
                if (!PInvoke.PrintWindow(hwnd, (HDC)hdc, RenderFullContent))
                    throw new WinMcpException(new WinMcpError(WinMcpErrorCode.InternalError, "PrintWindow failed to render the window."));
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
            return (bitmap, new Rect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top));
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static WinMcpException NotResponding(WindowHandle window) =>
        new(new WinMcpError(WinMcpErrorCode.TargetNotResponding,
            $"Window {window} is not responding, so it can't render a screenshot.",
            Hint: "Retry once the application responds again (inspect_window shows 'responding')."));
}
