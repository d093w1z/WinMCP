using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Identifiers;
using FlaUI.UIA3;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using FlaApp = FlaUI.Core.Application;

namespace UiaSpike;

internal sealed record Node(ControlType Type, string Name, string AutomationId, string ClassName, nint Hwnd, Rectangle Bounds, bool Enabled, bool Offscreen)
{
    public List<Node> Children { get; } = new();
    public IEnumerable<Node> Flatten() => Children.SelectMany(c => c.Flatten()).Prepend(this);
}

internal static class Program
{
    private const int Runs = 5;

    [MTAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "form")
        {
            SpikeForm.Run();
            return 0;
        }

        using var automation = new UIA3Automation();
        Section("Environment");
        Console.WriteLine($"- .NET {Environment.Version}, OS {Environment.OSVersion.Version}, 64-bit process: {Environment.Is64BitProcess}");
        Console.WriteLine($"- Main thread apartment: {Thread.CurrentThread.GetApartmentState()}");
        Console.WriteLine($"- DPI awareness: {PInvoke.GetAwarenessFromDpiAwarenessContext(PInvoke.GetThreadDpiAwarenessContext())}, system DPI: {PInvoke.GetDpiForSystem()}");
        var timeoutMembers = string.Join(", ", TimeoutMembers());
        Console.WriteLine($"- FlaUI timeout-related members on UIA3Automation: {(timeoutMembers.Length > 0 ? timeoutMembers : "(none)")}");

        RunForm(automation);
        RunCharmap(automation);
        return 0;
    }

    // ---------------------------------------------------------------- WinForms scratch form

    private static void RunForm(UIA3Automation automation)
    {
        var app = FlaApp.Launch(new ProcessStartInfo(Environment.ProcessPath!, "form") { UseShellExecute = false, CreateNoWindow = true });
        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException("Spike form did not appear");
            Section("WinForms spike form");
            DumpAndMeasure(automation, window);
            DpiReport(window);
            Interact(window);
            HangTest(automation, window);
        }
        finally
        {
            app.Kill();
            app.Dispose();
        }
    }

    private static void Interact(Window window)
    {
        Sub("Golden scenario via UIA patterns (live, uncached)");
        var sw = Stopwatch.StartNew();
        var name = Find(window, "nameTextBox");
        var combo = Find(window, "typeComboBox");
        var check = Find(window, "enableCheckBox");
        var apply = Find(window, "applyButton");
        var status = Find(window, "statusLabel");
        var events = Find(window, "eventCounterLabel");
        Console.WriteLine($"- Locate 6 elements by AutomationId: {sw.Elapsed.TotalMilliseconds:F0} ms");
        Console.WriteLine($"- nameTextBox UIA Name (label heuristic): \"{name.Properties.Name.ValueOrDefault}\", LabeledBy: {Describe(name.Properties.LabeledBy.ValueOrDefault)}");
        Console.WriteLine($"- Patterns on combo: {string.Join(", ", combo.GetSupportedPatterns().Select(p => p.Name))}");
        Console.WriteLine($"- Events before: {events.Properties.Name.Value}");

        Timed("ValuePattern.SetValue(\"Mukesh\")", () => name.Patterns.Value.Pattern.SetValue("Mukesh"));
        Console.WriteLine($"  value_after = \"{name.Patterns.Value.Pattern.Value.Value}\"");

        var items = combo.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
        Console.WriteLine($"- ListItems visible under collapsed combo: {items.Length} [{string.Join(", ", items.Select(i => i.Properties.Name.ValueOrDefault))}], expand state: {combo.Patterns.ExpandCollapse.PatternOrDefault?.ExpandCollapseState.ValueOrDefault}");
        var html = items.FirstOrDefault(i => i.Properties.Name.ValueOrDefault == "HTML");
        if (html is null)
        {
            Console.WriteLine("  HTML item not found while collapsed; expanding");
            combo.Patterns.ExpandCollapse.Pattern.Expand();
            html = combo.FindFirstDescendant(cf => cf.ByName("HTML").And(cf.ByControlType(ControlType.ListItem)));
        }
        Timed("SelectionItemPattern.Select(HTML)", () => html!.Patterns.SelectionItem.Pattern.Select());
        Console.WriteLine($"  expand state right after Select: {combo.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value}");
        if (combo.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value != ExpandCollapseState.Collapsed)
        {
            combo.Patterns.ExpandCollapse.Pattern.Collapse();
            Console.WriteLine($"  after Collapse(): {combo.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value}");
        }
        Console.WriteLine($"  combo value_after = \"{combo.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault}\", selection = \"{string.Join(",", combo.Patterns.Selection.PatternOrDefault?.Selection.ValueOrDefault?.Select(s => s.Properties.Name.ValueOrDefault) ?? Array.Empty<string>())}\"");

        Console.WriteLine($"- Checkbox toggle state: {check.Patterns.Toggle.Pattern.ToggleState.Value}");
        Timed("InvokePattern.Invoke(Apply)", () => apply.Patterns.Invoke.Pattern.Invoke());
        var poll = Stopwatch.StartNew();
        string statusText;
        while (!(statusText = status.Properties.Name.Value).Contains("Applied") && poll.ElapsedMilliseconds < 3000) Thread.Sleep(10);
        Console.WriteLine($"  status after {poll.ElapsedMilliseconds} ms: \"{statusText}\"");
        Console.WriteLine($"- Events after: {events.Properties.Name.Value}");
        Console.WriteLine($"- Golden scenario total: {sw.Elapsed.TotalMilliseconds:F0} ms");

        var hidden = window.FindFirstDescendant(cf => cf.ByAutomationId("hiddenTextBox"));
        Console.WriteLine($"- Hidden (Visible=false) textbox in control view: {(hidden is null ? "absent" : "PRESENT")}");
        var disabled = Find(window, "advancedButton");
        Console.WriteLine($"- Disabled button: IsEnabled={disabled.Properties.IsEnabled.Value}, Invoke pattern supported={disabled.Patterns.Invoke.IsSupported}");
        try
        {
            disabled.Patterns.Invoke.Pattern.Invoke();
            Thread.Sleep(200);
            Console.WriteLine($"  Invoke on disabled button did NOT throw; status now \"{status.Properties.Name.Value}\"");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Invoke on disabled button threw {ex.GetType().Name} (0x{ex.HResult:X8}); status \"{status.Properties.Name.Value}\"");
        }
    }

    private static void HangTest(UIA3Automation automation, Window window)
    {
        Sub("Hung target (UI thread sleeps 8 s)");
        var name = Find(window, "nameTextBox");
        var hangHwnd = (HWND)Find(window, "hangButton").Properties.NativeWindowHandle.Value;
        var formHwnd = (HWND)window.Properties.NativeWindowHandle.Value;

        HangOnce("default UIA timeouts");
        var tx = typeof(UIA3Automation).GetProperty("TransactionTimeout");
        if (tx is not null && tx.CanWrite)
        {
            tx.SetValue(automation, TimeSpan.FromSeconds(2));
            HangOnce("TransactionTimeout = 2 s");
        }
        else
        {
            Console.WriteLine("- UIA3Automation.TransactionTimeout not available as a settable property");
        }

        void HangOnce(string label)
        {
            PInvoke.PostMessage(hangHwnd, PInvoke.BM_CLICK, default, default);
            Thread.Sleep(500);
            var sw = Stopwatch.StartNew();
            var read = Task.Run(() =>
            {
                try { return "ok: \"" + name.Properties.Name.Value + "\""; }
                catch (Exception ex) { return $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}"; }
            });
            var finished = read.Wait(TimeSpan.FromSeconds(15));
            Console.WriteLine($"- [{label}] live property read took {sw.Elapsed.TotalMilliseconds:F0} ms -> {(finished ? read.Result : "still blocked after 15 s")}");
            Thread.Sleep(Math.Max(0, 6000 - (int)sw.ElapsedMilliseconds));
            Console.WriteLine($"  IsHungAppWindow ~6 s into hang: {(bool)PInvoke.IsHungAppWindow(formHwnd)}");
            WaitResponsive(name);
        }
    }

    private static void WaitResponsive(AutomationElement element)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(20))
        {
            try { _ = element.Properties.Name.Value; return; }
            catch { Thread.Sleep(200); }
        }
    }

    // ---------------------------------------------------------------- classic Win32 (charmap)

    private static void RunCharmap(UIA3Automation automation)
    {
        var app = FlaApp.Launch("charmap.exe");
        try
        {
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException("charmap did not appear");
            Section("Classic Win32 dialog: charmap.exe (MFC stand-in)");
            var tree = DumpAndMeasure(automation, window);
            Correlate(tree);
            DpiReport(window);
        }
        finally
        {
            if (!app.Close()) app.Kill();
            app.Dispose();
        }
    }

    private static void Correlate(Node tree)
    {
        Sub("HWND ↔ control ID ↔ AutomationId");
        Console.WriteLine("| ControlType | Name | ClassName | HWND | GetDlgCtrlID | AutomationId | AutomationId == ctrl ID |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        var withoutHwnd = 0;
        foreach (var n in tree.Flatten().Skip(1))
        {
            if (n.Hwnd == 0) { withoutHwnd++; continue; }
            var id = PInvoke.GetDlgCtrlID((HWND)n.Hwnd);
            Console.WriteLine($"| {n.Type} | {Trunc(n.Name, 24)} | {n.ClassName} | 0x{n.Hwnd:X} | {id} | {n.AutomationId} | {(n.AutomationId == id.ToString() ? "yes" : "NO")} |");
        }
        Console.WriteLine($"\nElements without their own HWND (windowless): {withoutHwnd}");
    }

    // ---------------------------------------------------------------- shared measurement

    private static Node DumpAndMeasure(UIA3Automation automation, AutomationElement window)
    {
        Sub("Tree fetch: naive TreeWalker vs CacheRequest(Subtree)");
        var walker = automation.TreeWalkerFactory.GetControlViewWalker();
        var naive = Median(() => Naive(window, walker), out var naiveMs);
        var cached = Median(() => CachedDump(automation, window), out var cachedMs);
        var nNaive = naive.Flatten().Count();
        var nCached = cached.Flatten().Count();
        Console.WriteLine($"- Naive walk: {nNaive} nodes, median {naiveMs:F1} ms over {Runs} runs");
        Console.WriteLine($"- Cached:     {nCached} nodes, median {cachedMs:F1} ms over {Runs} runs  (speed-up x{naiveMs / Math.Max(cachedMs, 0.01):F1})");
        if (nNaive != nCached) Console.WriteLine("  WARNING: node counts differ between strategies");
        Sub("Control-view outline (cached)");
        Console.WriteLine("```");
        Print(cached, 0);
        Console.WriteLine("```");
        return cached;
    }

    private static Node Naive(AutomationElement e, FlaUI.Core.ITreeWalker walker)
    {
        var node = Read(e);
        for (var c = walker.GetFirstChild(e); c is not null; c = walker.GetNextSibling(c))
            node.Children.Add(Naive(c, walker));
        return node;
    }

    private static Node CachedDump(UIA3Automation automation, AutomationElement window)
    {
        var lib = automation.PropertyLibrary.Element;
        var request = new CacheRequest
        {
            TreeScope = TreeScope.Subtree,
            TreeFilter = new NotCondition(new PropertyCondition(lib.IsControlElement, false)),
        };
        foreach (var p in new PropertyId[] { lib.ControlType, lib.Name, lib.AutomationId, lib.ClassName, lib.NativeWindowHandle, lib.BoundingRectangle, lib.IsEnabled, lib.IsOffscreen })
            request.Add(p);
        using (request.Activate())
        {
            var root = window.FindFirst(TreeScope.Element, TrueCondition.Default);
            return Build(root);
        }

        static Node Build(AutomationElement e)
        {
            var node = Read(e);
            foreach (var c in e.CachedChildren) node.Children.Add(Build(c));
            return node;
        }
    }

    private static Node Read(AutomationElement e) => new(
        e.Properties.ControlType.ValueOrDefault,
        e.Properties.Name.ValueOrDefault ?? "",
        e.Properties.AutomationId.ValueOrDefault ?? "",
        e.Properties.ClassName.ValueOrDefault ?? "",
        e.Properties.NativeWindowHandle.ValueOrDefault,
        e.Properties.BoundingRectangle.ValueOrDefault,
        e.Properties.IsEnabled.ValueOrDefault,
        e.Properties.IsOffscreen.ValueOrDefault);

    private static unsafe void DpiReport(AutomationElement window)
    {
        Sub("DPI / bounds agreement (top-level window)");
        var hwnd = (HWND)window.Properties.NativeWindowHandle.Value;
        PInvoke.GetWindowRect(hwnd, out var wr);
        RECT frame;
        PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &frame, (uint)sizeof(RECT));
        var uia = window.Properties.BoundingRectangle.Value;
        Console.WriteLine($"- GetDpiForWindow: {PInvoke.GetDpiForWindow(hwnd)}");
        Console.WriteLine($"- UIA BoundingRectangle: {Fmt(uia.X, uia.Y, uia.Width, uia.Height)}");
        Console.WriteLine($"- GetWindowRect:         {Fmt(wr.left, wr.top, wr.right - wr.left, wr.bottom - wr.top)}");
        Console.WriteLine($"- DWM extended frame:    {Fmt(frame.left, frame.top, frame.right - frame.left, frame.bottom - frame.top)}");

        static string Fmt(int x, int y, int w, int h) => $"x={x} y={y} w={w} h={h}";
    }

    // ---------------------------------------------------------------- helpers

    private static AutomationElement Find(AutomationElement root, string automationId) =>
        root.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
        ?? throw new InvalidOperationException($"Element #{automationId} not found");

    private static string Describe(AutomationElement? e) =>
        e is null ? "(none)" : $"{e.Properties.ControlType.ValueOrDefault} \"{e.Properties.Name.ValueOrDefault}\"";

    private static T Median<T>(Func<T> f, out double medianMs)
    {
        var times = new List<double>();
        T result = f(); // warm-up, not timed
        for (var i = 0; i < Runs; i++)
        {
            var sw = Stopwatch.StartNew();
            result = f();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        medianMs = times[times.Count / 2];
        return result;
    }

    private static void Timed(string label, Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        Console.WriteLine($"- {label}: {sw.Elapsed.TotalMilliseconds:F1} ms");
    }

    private static void Print(Node n, int depth)
    {
        var id = n.AutomationId.Length > 0 ? $" #{n.AutomationId}" : "";
        var hwnd = n.Hwnd != 0 ? $" hwnd=0x{n.Hwnd:X}" : "";
        var flags = (n.Enabled ? "" : " disabled") + (n.Offscreen ? " offscreen" : "");
        Console.WriteLine($"{new string(' ', depth * 2)}{n.Type} \"{Trunc(n.Name, 40)}\"{id} [{n.ClassName}]{hwnd}{flags}");
        foreach (var c in n.Children) Print(c, depth + 1);
    }

    private static IEnumerable<string> TimeoutMembers() =>
        typeof(UIA3Automation).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.Name} ({p.PropertyType.Name}{(p.CanWrite ? ", settable" : "")})");

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
    private static void Section(string title) => Console.WriteLine($"\n## {title}\n");
    private static void Sub(string title) => Console.WriteLine($"\n### {title}\n");
}
