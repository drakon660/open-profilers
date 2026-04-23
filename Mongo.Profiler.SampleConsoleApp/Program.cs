using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mongo.Profiler.SampleConsoleApp.Commands;
using Mongo.Profiler.SampleConsoleApp.ConsoleUi;
using Mongo.Profiler.SampleConsoleApp.Infrastructure;
using Mongo.Profiler.SampleConsoleApp.Models;
using MongoDB.Driver;
using Spectre.Console;

using var host = SampleHost.Build(args);
await host.StartAsync();

var options = host.Services.GetRequiredService<IOptions<SampleOptions>>().Value;
var relayManager = host.Services.GetRequiredService<GrpcRelayManager>();
var context = new SampleContext(
    host.Services.GetRequiredService<IMongoClient>(),
    options,
    relayManager);

AnsiConsole.Write(new FigletText("Mongo Profiler").Color(Color.Green));
AnsiConsole.MarkupLineInterpolated($"[grey]Relay (on-demand):[/] localhost:{options.GrpcPort} (use the [green]Relay[/] commands to start)");
AnsiConsole.MarkupLineInterpolated($"[grey]Mongo:[/] {options.ConnectionString}");
AnsiConsole.MarkupLineInterpolated($"[grey]Target:[/] {options.DatabaseName}.{options.CollectionName}");
AnsiConsole.WriteLine();

await SampleConsoleRunner.RunAsync(context, SampleCommandCatalog.Build());

await relayManager.DisposeAsync();
await host.StopAsync();
