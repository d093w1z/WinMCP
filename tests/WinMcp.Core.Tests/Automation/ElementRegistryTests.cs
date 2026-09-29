using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;

namespace WinMcp.Core.Tests.Automation;

public sealed class ElementRegistryTests
{
    private static readonly WindowHandle Window = new(0x10);

    [Fact]
    public void Same_element_always_gets_the_same_ref()
    {
        var registry = new ElementRegistry();

        var first = registry.GetOrAdd(new ElementKey(Window, "42.1"));
        var other = registry.GetOrAdd(new ElementKey(Window, "42.2"));
        var again = registry.GetOrAdd(new ElementKey(Window, "42.1"));

        Assert.Equal("e1", first);
        Assert.Equal("e2", other);
        Assert.Equal(first, again);
    }

    [Fact]
    public void Same_runtime_id_in_different_windows_gets_different_refs()
    {
        var registry = new ElementRegistry();

        Assert.NotEqual(
            registry.GetOrAdd(new ElementKey(Window, "42.1")),
            registry.GetOrAdd(new ElementKey(new WindowHandle(0x20), "42.1")));
    }

    [Theory]
    [InlineData("e1")]
    [InlineData(" E1 ")]
    public void Resolves_refs_leniently(string reference)
    {
        var registry = new ElementRegistry();
        var key = new ElementKey(Window, "42.1");
        registry.GetOrAdd(key);

        Assert.Equal(key, registry.Resolve(reference));
    }

    [Theory]
    [InlineData("e99")]
    [InlineData("applyButton")]
    public void Unknown_ref_is_element_not_found(string reference)
    {
        var ex = Assert.Throws<WinMcpException>(() => new ElementRegistry().Resolve(reference));

        Assert.Equal(WinMcpErrorCode.ElementNotFound, ex.Error.Code);
    }
}
