using Xunit.Sdk;
using Xunit.v3;

// GUI tests share one desktop, focus and input queue: never run them in parallel.
[assembly: Parallelization(Mode = ParallelMode.None)]
