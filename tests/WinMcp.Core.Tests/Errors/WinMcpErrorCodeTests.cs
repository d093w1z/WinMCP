using WinMcp.Core.Errors;

namespace WinMcp.Core.Tests.Errors;

public sealed class WinMcpErrorCodeTests
{
    public static TheoryData<WinMcpErrorCode> AllCodes() => new(Enum.GetValues<WinMcpErrorCode>());

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void Every_code_has_a_category(WinMcpErrorCode code) =>
        Assert.True(Enum.IsDefined(code.Category()));

    [Theory]
    [InlineData(WinMcpErrorCode.ElementNotFound, "ELEMENT_NOT_FOUND")]
    [InlineData(WinMcpErrorCode.Timeout, "TIMEOUT")]
    [InlineData(WinMcpErrorCode.AccessDeniedElevated, "ACCESS_DENIED_ELEVATED")]
    [InlineData(WinMcpErrorCode.InternalError, "INTERNAL_ERROR")]
    public void Wire_name_is_screaming_snake_case(WinMcpErrorCode code, string expected) =>
        Assert.Equal(expected, code.ToWireName());

    [Fact]
    public void Wire_names_are_unique() =>
        Assert.Equal(
            Enum.GetValues<WinMcpErrorCode>().Length,
            Enum.GetValues<WinMcpErrorCode>().Select(c => c.ToWireName()).Distinct().Count());

    [Theory]
    [InlineData(WinMcpErrorCode.ElementDisabled, ErrorCategory.Caller)]
    [InlineData(WinMcpErrorCode.TargetNotAllowed, ErrorCategory.Policy)]
    [InlineData(WinMcpErrorCode.TargetNotResponding, ErrorCategory.Environment)]
    [InlineData(WinMcpErrorCode.InternalError, ErrorCategory.Internal)]
    public void Category_mapping(WinMcpErrorCode code, ErrorCategory expected) =>
        Assert.Equal(expected, code.Category());

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void Caller_policy_and_internal_errors_are_never_retryable_unchanged(WinMcpErrorCode code)
    {
        if (code.Category() != ErrorCategory.Environment)
            Assert.False(code.IsRetryable());
    }

    [Fact]
    public void Error_derives_category_and_retryability_from_code()
    {
        var error = new WinMcpError(WinMcpErrorCode.Timeout, "Timed out waiting for element.");

        Assert.Equal(ErrorCategory.Environment, error.Category);
        Assert.True(error.Retryable);
    }

    [Fact]
    public void Exception_carries_error()
    {
        var error = new WinMcpError(WinMcpErrorCode.ElementStale, "Element e17 no longer exists.", Hint: "Call find_elements again.");

        var exception = new WinMcpException(error);

        Assert.Same(error, exception.Error);
        Assert.Equal(error.Message, exception.Message);
    }
}
