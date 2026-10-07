using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.JobObjects;

namespace WinMcp.Testing;

/// <summary>
/// Applications launched by tests are placed in a Windows job that is killed when the test process exits — normally,
/// on a crash, on a timeout, or when a run is interrupted — so no test app is ever left running on the desktop.
/// </summary>
internal static unsafe class ChildProcessJob
{
    // Held open for the life of the test process; the OS closes it on exit, which kills every process in the job.
    private static readonly Microsoft.Win32.SafeHandles.SafeFileHandle Job = Create();

    public static void Add(int processId)
    {
        using var process = Process.GetProcessById(processId);
        if (!PInvoke.AssignProcessToJobObject(Job, process.SafeHandle))
            throw new InvalidOperationException($"Could not assign process {processId} to the test job (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).");
    }

    private static Microsoft.Win32.SafeHandles.SafeFileHandle Create()
    {
        var job = PInvoke.CreateJobObject(null, (string?)null);
        if (job.IsInvalid)
            throw new InvalidOperationException("Could not create the test job object.");

        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        // No SafeHandle overload is generated for this API; the handle is kept alive for the process lifetime anyway.
        if (!PInvoke.SetInformationJobObject((HANDLE)job.DangerousGetHandle(), JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)))
            throw new InvalidOperationException("Could not configure the test job object.");
        return job;
    }
}
