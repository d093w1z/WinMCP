using WinMcp.Core.Policy;

namespace WinMcp.Core.Tests.Policy;

public sealed class WinMcpOptionsTests
{
    [Fact]
    public void Defaults_to_observe_with_empty_allowlist()
    {
        var options = WinMcpOptions.Parse([]);

        Assert.Equal(ServerMode.Observe, options.Mode);
        Assert.Empty(options.Allow);
    }

    [Fact]
    public void Parses_mode_and_repeated_allow_entries()
    {
        var options = WinMcpOptions.Parse(["--mode", "CONTROL", "--allow", "WinMcp.TestApp", "--allow", @"C:\Apps\Other.exe"]);

        Assert.Equal(ServerMode.Control, options.Mode);
        Assert.Equal(["WinMcp.TestApp", @"C:\Apps\Other.exe"], options.Allow);
    }

    [Fact]
    public void Parses_symbols_per_process_normalizing_the_name()
    {
        var header = Path.GetTempFileName();
        try
        {
            var options = WinMcpOptions.Parse(["--symbols", $"MfcTestApp.exe=\"{header}\""]);

            Assert.Equal(Path.GetFullPath(header), options.Symbols["mfctestapp"]);
        }
        finally
        {
            File.Delete(header);
        }
    }

    [Theory]
    [InlineData("--symbols", "MfcTestApp")]                      // no '='
    [InlineData("--symbols", "=C:\\resource.h")]                 // no process
    [InlineData("--symbols", "MfcTestApp=C:\\nope\\resource.h")] // missing file
    public void Rejects_invalid_symbols(params string[] args) =>
        Assert.Throws<ArgumentException>(() => WinMcpOptions.Parse(args));

    [Theory]
    [InlineData("--mode", "control")]                  // control needs an allowlist
    [InlineData("--mode", "admin")]
    [InlineData("--mode", "1")]                        // numeric enum values must not enable control mode
    [InlineData("--allow")]                            // missing value
    [InlineData("--allow", "--mode", "observe")]       // flag instead of value
    [InlineData("--allow", " ")]
    [InlineData("--verbose")]
    public void Rejects_invalid_arguments(params string[] args) =>
        Assert.Throws<ArgumentException>(() => WinMcpOptions.Parse(args));
}
