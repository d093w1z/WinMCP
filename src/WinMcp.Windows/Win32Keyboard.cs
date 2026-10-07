using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using WinMcp.Core.Automation;
using WinMcp.Core.Errors;
using WindowHandle = WinMcp.Core.Desktop.WindowHandle;

namespace WinMcp.Windows;

/// <summary>
/// Keyboard input via SendInput. SendInput goes to whatever window is in the foreground, so the only safe way to use it
/// is to verify, immediately before every chunk, that the foreground still belongs to the target — and stop otherwise.
/// </summary>
public sealed unsafe class Win32Keyboard : IKeyboard
{
    private const int TextChunk = 32;
    private const ushort VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkReturn = 0x0D, VkTab = 0x09;

    /// <summary>Keys that need KEYEVENTF_EXTENDEDKEY to be told apart from their numeric-keypad twins.</summary>
    private static readonly HashSet<ushort> ExtendedKeys = [0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5D];

    public Task<KeyboardOutcome> SendAsync(WindowHandle window, KeyInput input, CancellationToken cancellationToken) =>
        Task.Run(() => Send((HWND)(nint)window.Value, window, input), cancellationToken);

    private static KeyboardOutcome Send(HWND hwnd, WindowHandle window, KeyInput input)
    {
        if (!PInvoke.IsWindow(hwnd))
            throw new WinMcpException(new WinMcpError(WinMcpErrorCode.WindowClosed, $"Window {window} closed."));
        if (PInvoke.IsIconic(hwnd))
            throw new WinMcpException(new WinMcpError(WinMcpErrorCode.WindowMinimized,
                $"Window {window} is minimized and can't receive keyboard input.", Hint: "Ask the user to restore it."));
        if (!Foreground.Bring(hwnd))
            throw FocusFailed(window, 0, "Windows refused to bring it to the foreground", Foreground.RefusedHint());

        var chunks = input switch
        {
            KeyInput.Text text => TextChunks(text.Value),
            KeyInput.Chords chords => chords.Sequence.Select(ChordInputs).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };

        for (var i = 0; i < chunks.Count; i++)
        {
            if (!Foreground.Is(hwnd))
                throw FocusFailed(window, i, $"another window took the foreground after {i} of {chunks.Count} parts were sent",
                    "Something else (possibly a dialog of the application) has focus. Check with list_windows before retrying; the partial input already sent was not undone.");
            SendInputs(chunks[i]);
        }
        return new KeyboardOutcome("win32.SendInput", chunks.Count);
    }

    private static List<INPUT[]> TextChunks(string text)
    {
        var chunks = new List<INPUT[]>();
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        for (var start = 0; start < normalized.Length; start += TextChunk)
        {
            var inputs = new List<INPUT>();
            foreach (var c in normalized.AsSpan(start, Math.Min(TextChunk, normalized.Length - start)))
            {
                // Edit controls expect real Enter/Tab key presses, not the characters.
                if (c == '\n') inputs.AddRange(KeyPress(VkReturn));
                else if (c == '\t') inputs.AddRange(KeyPress(VkTab));
                else inputs.AddRange(UnicodePress(c));
            }
            chunks.Add([.. inputs]);
        }
        return chunks;
    }

    private static INPUT[] ChordInputs(KeyChord chord)
    {
        var modifiers = new List<ushort>();
        if (chord.Ctrl) modifiers.Add(VkControl);
        if (chord.Shift) modifiers.Add(VkShift);
        if (chord.Alt) modifiers.Add(VkMenu);

        var inputs = new List<INPUT>();
        inputs.AddRange(modifiers.Select(m => Key(m, up: false)));
        inputs.AddRange(KeyPress(chord.VirtualKey));
        inputs.AddRange(Enumerable.Reverse(modifiers).Select(m => Key(m, up: true)));
        return [.. inputs];
    }

    private static IEnumerable<INPUT> KeyPress(ushort vk) => [Key(vk, up: false), Key(vk, up: true)];

    private static INPUT Key(ushort vk, bool up)
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        input.Anonymous.ki.wVk = (VIRTUAL_KEY)vk;
        input.Anonymous.ki.wScan = (ushort)PInvoke.MapVirtualKey(vk, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC);
        input.Anonymous.ki.dwFlags = (up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0)
                                     | (ExtendedKeys.Contains(vk) ? KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY : 0);
        return input;
    }

    private static IEnumerable<INPUT> UnicodePress(char c)
    {
        foreach (var up in new[] { false, true })
        {
            var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
            input.Anonymous.ki.wScan = c;
            input.Anonymous.ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE | (up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0);
            yield return input;
        }
    }

    private static void SendInputs(INPUT[] inputs)
    {
        // SendInput fails as a whole when another thread blocks input; UIPI (elevated targets) blocks it silently.
        if (PInvoke.SendInput(inputs, sizeof(INPUT)) != inputs.Length)
            throw new WinMcpException(new WinMcpError(WinMcpErrorCode.FocusFailed, "Windows rejected the keyboard input.",
                Hint: "Input may be blocked (e.g. the workstation is locked or the target runs elevated)."));
    }

    private static WinMcpException FocusFailed(WindowHandle window, int sent, string why, string hint) =>
        new(new WinMcpError(WinMcpErrorCode.FocusFailed, $"Stopped typing into {window}: {why}.", Hint: hint,
            Details: new Dictionary<string, object?> { ["parts_sent"] = sent }));
}
