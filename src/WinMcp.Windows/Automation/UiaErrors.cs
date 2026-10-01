namespace WinMcp.Windows.Automation;

internal static class UiaErrors
{
    /// <summary>UIA_E_TIMEOUT: the target didn't answer within the UIA transaction/connection timeout.</summary>
    private const int UiaTimeout = unchecked((int)0x80131505);

    public static bool IsTimeout(Exception ex) => ex is TimeoutException || ex.HResult == UiaTimeout;
}
