using System.Collections.Concurrent;
using FlaUI.UIA3;
using WinMcp.Core.Errors;

namespace WinMcp.Windows.Automation;

/// <summary>
/// Runs all UI Automation work on one dedicated MTA thread (plan §A.3). UIA's own transaction timeout bounds most
/// calls against a hung target; if a call still exceeds the hard timeout, the thread is abandoned and a fresh one
/// serves later calls, so a single frozen application can't wedge the server.
/// </summary>
public sealed class AutomationDispatcher : IDisposable
{
    private readonly TimeSpan _uiaTimeout;
    private readonly TimeSpan _hardTimeout;
    private readonly Lock _lock = new();
    private Worker _worker;
    private bool _disposed;

    /// <param name="uiaTimeout">UIA connection and transaction timeout per cross-process call.</param>
    /// <param name="hardTimeout">Upper bound for a whole unit of work before its thread is abandoned.</param>
    public AutomationDispatcher(TimeSpan uiaTimeout, TimeSpan hardTimeout)
    {
        _uiaTimeout = uiaTimeout;
        _hardTimeout = hardTimeout;
        _worker = new Worker(uiaTimeout);
    }

    public TimeSpan UiaTimeout => _uiaTimeout;

    public async Task<T> RunAsync<T>(Func<UIA3Automation, T> work, CancellationToken cancellationToken)
    {
        Worker worker;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            worker = _worker;
        }

        var task = worker.Enqueue(work);
        var winner = await Task.WhenAny(task, Task.Delay(_hardTimeout, cancellationToken));
        if (winner == task)
            return await task;

        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            if (!_disposed && ReferenceEquals(_worker, worker))
            {
                worker.Abandon();
                _worker = new Worker(_uiaTimeout);
            }
        }
        throw new WinMcpException(new WinMcpError(
            WinMcpErrorCode.TargetNotResponding,
            $"The target application did not answer within {_hardTimeout.TotalSeconds:F0} s.",
            Hint: "The application may be busy or hung. Retry later."));
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _worker.Abandon();
        }
    }

    private sealed class Worker
    {
        private readonly BlockingCollection<Action<UIA3Automation>> _queue = [];

        public Worker(TimeSpan uiaTimeout)
        {
            var thread = new Thread(() => Run(uiaTimeout)) { IsBackground = true, Name = "WinMCP UI Automation" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        public Task<T> Enqueue<T>(Func<UIA3Automation, T> work)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _queue.Add(automation =>
                {
                    try { completion.SetResult(work(automation)); }
                    catch (Exception ex) { completion.SetException(ex); }
                });
            }
            catch (InvalidOperationException)
            {
                // Abandoned between the caller reading _worker and enqueueing.
                completion.SetException(new WinMcpException(new WinMcpError(
                    WinMcpErrorCode.TargetNotResponding, "UI Automation was reset after a previous hang.", Hint: "Retry.")));
            }
            return completion.Task;
        }

        /// <summary>Stops accepting work; the thread exits after its current item, whenever that returns.</summary>
        public void Abandon() => _queue.CompleteAdding();

        private void Run(TimeSpan uiaTimeout)
        {
            // UIA reports coordinates in the client thread's DPI context; bounds must be physical pixels like DWM's.
            DpiScope.SetForCurrentThread();
            using var automation = new UIA3Automation
            {
                ConnectionTimeout = uiaTimeout,
                TransactionTimeout = uiaTimeout,
            };
            foreach (var item in _queue.GetConsumingEnumerable())
                item(automation);
        }
    }
}
