using System.Drawing;
using System.Drawing.Imaging;
using FlaUI.Core.Definitions;
using WinMcp.Core.Automation;
using WinMcp.Core.Capture;
using WinMcp.Core.Errors;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>Plan §E.3 test 19 and M7 specifics, against the live TestApp (and charmap as an overlapping window).</summary>
[Trait("Category", "Windows")]
public sealed class ScreenshotTests : IClassFixture<TestAppSession>, IDisposable
{
    private readonly TestAppSession _app;
    private readonly WinMcpServices _services = new();
    private readonly ScreenshotService _screenshots;

    public ScreenshotTests(TestAppSession app)
    {
        _app = app;
        _app.Reset();
        _screenshots = new ScreenshotService(_services.Windows, _services.Tree, new PrintWindowCapture());
    }

    public void Dispose() => _services.Dispose();

    private Task<Screenshot> Capture(ElementLocator? locator = null, int padding = 0, int maxEdge = ScreenshotService.DefaultMaxEdge) =>
        _screenshots.CaptureAsync(_app.WindowHandle.ToString(), null, locator ?? new ElementLocator(), padding, maxEdge, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Window_capture_matches_the_visible_window_and_is_not_blank()
    {
        var window = _services.Windows.Inspect(_app.WindowHandle.ToString()).Window;

        var shot = await Capture(maxEdge: 4096); // no downscaling: the v2 window exceeds 1280 px at 150%
        using var image = Decode(shot.Png, "testapp-window");

        Assert.Equal("PrintWindow", shot.Info.Method);
        Assert.Equal(window.Bounds, shot.Info.SourceBounds); // DWM visible frame, not the invisible resize border
        Assert.Equal(1.0, shot.Info.Scale);
        Assert.Equal((window.Bounds.Width, window.Bounds.Height), (image.Width, image.Height));
        Assert.True(DistinctColours(image) > 10, "capture looks blank");
    }

    [Fact]
    public async Task Element_capture_covers_the_element_plus_padding()
    {
        var apply = await _services.Tree.InspectAsync(_app.WindowHandle.ToString(), null, new ElementLocator(AutomationId: "applyButton"), TestContext.Current.CancellationToken);

        var shot = await Capture(new ElementLocator(AutomationId: "applyButton"), padding: 4);
        using var image = Decode(shot.Png, "testapp-apply-button");

        Assert.Equal(apply.Bounds.Width + 8, image.Width);
        Assert.Equal(apply.Bounds.Height + 8, image.Height);
        Assert.Equal(apply.Ref, shot.Info.Element);
    }

    [Fact]
    public async Task Max_edge_downscales_proportionally()
    {
        var shot = await Capture(maxEdge: 200);
        using var image = Decode(shot.Png, "testapp-window-200");

        Assert.Equal(200, Math.Max(image.Width, image.Height));
        Assert.True(shot.Info.Scale < 1);
        Assert.Equal(shot.Info.SourceBounds.Width * shot.Info.Scale, image.Width, tolerance: 1.0);
    }

    [Fact]
    public async Task Covered_window_captures_its_own_content_not_the_window_on_top()
    {
        using var alone = Decode((await Capture()).Png, "testapp-alone");

        using var cover = new CharmapSession();
        var window = _services.Windows.Inspect(_app.WindowHandle.ToString()).Window;
        cover.Window.Patterns.Transform.Pattern.Move(window.Bounds.X + 20, window.Bounds.Y + 20);
        cover.Window.Focus();
        using var covered = Decode((await Capture()).Png, "testapp-covered-by-charmap");

        Assert.Equal((alone.Width, alone.Height), (covered.Width, covered.Height));
        // Losing focus greys the title bar (seen in M7's saved captures), so compare the client area only.
        // Charmap was placed over it from 20 px in, so any leaked pixels would show up there.
        var titleBar = 40 * window.Dpi / 96;
        var changed = DifferingPixelFraction(alone, covered, fromRow: titleBar);
        Assert.True(changed < 0.02, $"{changed:P1} of client-area pixels changed");
    }

    [Fact]
    public async Task Minimized_window_is_an_error_not_an_empty_image()
    {
        var visual = _app.Window.Patterns.Window.Pattern;
        visual.SetWindowVisualState(WindowVisualState.Minimized);
        try
        {
            TestAppSession.WaitUntil(() => _services.Windows.Inspect(_app.WindowHandle.ToString()).Window.Minimized, "window is minimized");

            var ex = await Assert.ThrowsAsync<WinMcpException>(() => Capture());

            Assert.Equal(WinMcpErrorCode.WindowMinimized, ex.Error.Code);
        }
        finally
        {
            visual.SetWindowVisualState(WindowVisualState.Normal);
            TestAppSession.WaitUntil(() => !_services.Windows.Inspect(_app.WindowHandle.ToString()).Window.Minimized, "window is restored");
        }
    }

    private static Bitmap Decode(byte[] png, string name)
    {
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png.Take(4)); // PNG signature
        var directory = Path.Combine(Path.GetTempPath(), "winmcp-screenshots");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), png); // kept for manual inspection
        return new Bitmap(new MemoryStream(png));
    }

    private static int DistinctColours(Bitmap image)
    {
        var colours = new HashSet<int>();
        for (var y = 0; y < image.Height; y += 3)
            for (var x = 0; x < image.Width; x += 3)
                colours.Add(image.GetPixel(x, y).ToArgb());
        return colours.Count;
    }

    private static double DifferingPixelFraction(Bitmap a, Bitmap b, int fromRow)
    {
        int differing = 0, total = 0;
        for (var y = fromRow; y < a.Height; y += 2)
            for (var x = 0; x < a.Width; x += 2, total++)
                if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb())
                    differing++;
        return (double)differing / total;
    }
}
