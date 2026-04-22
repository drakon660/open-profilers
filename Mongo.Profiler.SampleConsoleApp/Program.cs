using Microsoft.Extensions.DependencyInjection;
using Mongo.Profiler.SampleConsoleApp.Commands;
using Mongo.Profiler.SampleConsoleApp.ConsoleUi;
using Mongo.Profiler.SampleConsoleApp.Infrastructure;
using Mongo.Profiler.SampleConsoleApp.Models;
using MongoDB.Driver;
using Spectre.Console;

var options = SampleOptions.Load();
using var host = SampleHost.Build(args, options);
await host.StartAsync();

var context = new SampleContext(
    host.Services.GetRequiredService<IMongoClient>(),
    options);

AnsiConsole.Write(new FigletText("Mongo Profiler").Color(Color.Green));
AnsiConsole.MarkupLineInterpolated($"[grey]Relay:[/] localhost:{options.GrpcPort}");
AnsiConsole.MarkupLineInterpolated($"[grey]Mongo:[/] {options.ConnectionString}");
AnsiConsole.MarkupLineInterpolated($"[grey]Target:[/] {options.DatabaseName}.{options.CollectionName}");
AnsiConsole.WriteLine();

await SampleConsoleRunner.RunAsync(context, SampleCommandCatalog.Build());

await host.StopAsync();
