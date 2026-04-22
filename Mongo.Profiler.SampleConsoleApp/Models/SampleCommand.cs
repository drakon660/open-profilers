namespace Mongo.Profiler.SampleConsoleApp.Models;

internal sealed record SampleCommand(
    string Category,
    string Name,
    string Description,
    Func<SampleContext, Task<CommandResult>> RunAsync,
    bool IsExit = false);
