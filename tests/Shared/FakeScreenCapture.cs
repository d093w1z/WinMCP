using WinMcp.Core.Capture;
using WinMcp.Core.Desktop;

namespace WinMcp.Testing;

/// <summary>Records requested regions and returns a tiny fixed PNG.</summary>
internal sealed class FakeScreenCapture : IScreenCapture
{
    /// <summary>1×1 PNG.</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC");

    public List<(WindowHandle Window, Rect Region, int MaxEdge)> Requests { get; } = [];

    public Task<CapturedImage> CaptureAsync(WindowHandle window, Rect region, int maxEdge, CancellationToken cancellationToken)
    {
        Requests.Add((window, region, maxEdge));
        return Task.FromResult(new CapturedImage(Png, 1, 1, region, 1.0, "fake"));
    }
}
