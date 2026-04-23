using Mongo.Profiler.SampleConsoleApp.Models;

namespace Mongo.Profiler.SampleConsoleApp.Commands;

internal static partial class SampleCommands
{
    public static async Task<CommandResult> StartGrpcRelayAsync(SampleContext context)
    {
        if (context.Relay.IsRunning)
            return new TextResult($"gRPC relay already running on {context.Relay.Address}.");

        var started = await context.Relay.StartAsync();
        return started
            ? new TextResult($"gRPC relay started on {context.Relay.Address}.")
            : new TextResult("gRPC relay was already running.");
    }

    public static async Task<CommandResult> StopGrpcRelayAsync(SampleContext context)
    {
        if (!context.Relay.IsRunning)
            return new TextResult("gRPC relay is not running.");

        var stopped = await context.Relay.StopAsync();
        return stopped
            ? new TextResult("gRPC relay stopped.")
            : new TextResult("gRPC relay was not running.");
    }

    public static Task<CommandResult> GrpcRelayStatusAsync(SampleContext context)
    {
        var status = context.Relay.IsRunning
            ? $"running on {context.Relay.Address}"
            : $"stopped (default port {context.Relay.DefaultPort})";
        return Task.FromResult<CommandResult>(new TextResult($"gRPC relay: {status}."));
    }
}
