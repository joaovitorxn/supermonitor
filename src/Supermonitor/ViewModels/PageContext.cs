using Supermonitor.Core;

namespace Supermonitor.ViewModels;

public sealed record PageContext(AppHost Host, Func<bool> IsMinimized, Func<IDisposable> AcquireProcesses);
