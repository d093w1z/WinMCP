using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;

namespace WinMcp.Testing;

/// <summary>Records what would have been typed, and where.</summary>
internal sealed class FakeKeyboard : IKeyboard
{
    public List<(WindowHandle Window, KeyInput Input)> Sent { get; } = [];

    public Task<KeyboardOutcome> SendAsync(WindowHandle window, KeyInput input, CancellationToken cancellationToken)
    {
        Sent.Add((window, input));
        return Task.FromResult(new KeyboardOutcome("fake.SendInput", 1));
    }
}
