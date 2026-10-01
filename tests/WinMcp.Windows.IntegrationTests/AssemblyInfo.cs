using Xunit.Sdk;
using Xunit.v3;

// GUI tests share one desktop, focus and input queue: never run them in parallel.
[assembly: Parallelization(Mode = ParallelMode.None)]

// A stuck run fails loudly after WINMCP_TEST_TIMEOUT_MINUTES (default 5) instead of appearing frozen.
[assembly: AssemblyFixture(typeof(WinMcp.Testing.TestRunWatchdog))]
