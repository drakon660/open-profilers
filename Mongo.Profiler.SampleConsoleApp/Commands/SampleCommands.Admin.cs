using Mongo.Profiler.SampleConsoleApp.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using Spectre.Console;

namespace Mongo.Profiler.SampleConsoleApp.Commands;

internal static partial class SampleCommands
{
    public static Task<CommandResult> PingAsync(SampleContext context) =>
        RunDatabaseCommandAsync(context.Database, new BsonDocument("ping", 1));

    public static Task<CommandResult> BuildInfoAsync(SampleContext context) =>
        RunDatabaseCommandAsync(context.Database, new BsonDocument("buildInfo", 1));

    public static Task<CommandResult> ServerStatusAsync(SampleContext context) =>
        RunDatabaseCommandAsync(context.Database, new BsonDocument("serverStatus", 1));

    public static Task<CommandResult> ConnectionStatusAsync(SampleContext context) =>
        RunDatabaseCommandAsync(context.Database, new BsonDocument("connectionStatus", 1));

    public static async Task<CommandResult> ListDatabasesAsync(SampleContext context)
    {
        using var cursor = await context.Client.ListDatabasesAsync();
        return new DocumentsResult(await cursor.ToListAsync());
    }

    public static async Task<CommandResult> ListCollectionsAsync(SampleContext context)
    {
        using var cursor = await context.Database.ListCollectionsAsync();
        return new DocumentsResult(await cursor.ToListAsync());
    }

    public static Task<CommandResult> DatabaseStatsAsync(SampleContext context) =>
        RunDatabaseCommandAsync(context.Database, new BsonDocument("dbStats", 1));

    public static Task<CommandResult> CollectionStatsAsync(SampleContext context) =>
        RunDatabaseCommandAsync(context.Database, new BsonDocument("collStats", context.Options.CollectionName));

    public static Task<CommandResult> CurrentOpAsync(SampleContext context) =>
        RunDatabaseCommandAsync(context.Client.GetDatabase("admin"), new BsonDocument("currentOp", 1));

    public static async Task<CommandResult> RawCommandAsync(SampleContext context)
    {
        var json = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Mongo command JSON[/]")
                .DefaultValue("{ ping: 1 }")
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(json))
            json = "{ ping: 1 }";

        var command = BsonDocument.Parse(json);
        return new DocumentResult(await context.Database.RunCommandAsync<BsonDocument>(command));
    }

    private static async Task<CommandResult> RunDatabaseCommandAsync(IMongoDatabase database, BsonDocument command)
    {
        var result = await database.RunCommandAsync<BsonDocument>(command);
        return new DocumentResult(result);
    }
}
