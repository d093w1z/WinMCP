// A stuck run fails loudly after WINMCP_TEST_TIMEOUT_MINUTES (default 5) instead of appearing frozen.
[assembly: AssemblyFixture(typeof(WinMcp.Testing.TestRunWatchdog))]
