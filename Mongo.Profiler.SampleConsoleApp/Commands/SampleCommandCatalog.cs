using Mongo.Profiler.SampleConsoleApp.Models;

namespace Mongo.Profiler.SampleConsoleApp.Commands;

internal static class SampleCommandCatalog
{
    public static IReadOnlyList<SampleCommand> Build()
    {
        return
        [
            new("Setup", "Seed sample data", "Recreates the sample collection with predictable orders.", SampleCommands.SeedAsync),
            new("Setup", "Create collection", "Creates the configured sample collection if it is missing.", SampleCommands.CreateCollectionAsync),
            new("Setup", "Drop collection", "Drops the configured sample collection.", SampleCommands.DropCollectionAsync),
            new("Setup", "Create indexes", "Creates indexes for customer, status, createdAt, and totals.", SampleCommands.CreateIndexesAsync),
            new("Setup", "List indexes", "Lists indexes on the sample collection.", SampleCommands.ListIndexesAsync),
            new("Setup", "Drop sample index", "Drops the customer/status index if it exists.", SampleCommands.DropSampleIndexAsync),

            new("Read", "Find filtered", "Runs a filtered find with sort and limit.", SampleCommands.FindFilteredAsync),
            new("Read", "Find projection", "Runs a find with a projection.", SampleCommands.FindProjectionAsync),
            new("Read", "Find one and update", "Updates and returns a single document.", SampleCommands.FindOneAndUpdateAsync),
            new("Read", "Count documents", "Counts documents matching a filter.", SampleCommands.CountDocumentsAsync),
            new("Read", "Estimated count", "Runs estimated document count.", SampleCommands.EstimatedCountAsync),
            new("Read", "Distinct", "Reads distinct customer values.", SampleCommands.DistinctAsync),

            new("Write", "Insert one", "Inserts one generated order.", SampleCommands.InsertOneAsync),
            new("Write", "Insert many", "Inserts a small batch of generated orders.", SampleCommands.InsertManyAsync),
            new("Write", "Replace one", "Replaces one matching order.", SampleCommands.ReplaceOneAsync),
            new("Write", "Update one", "Updates one matching order.", SampleCommands.UpdateOneAsync),
            new("Write", "Update many", "Updates many matching orders.", SampleCommands.UpdateManyAsync),
            new("Write", "Delete one", "Deletes one temporary generated order.", SampleCommands.DeleteOneAsync),
            new("Write", "Delete many", "Deletes temporary generated orders.", SampleCommands.DeleteManyAsync),
            new("Write", "Bulk write", "Runs mixed insert, update, replace, and delete models.", SampleCommands.BulkWriteAsync),

            new("Aggregate", "Aggregate summary", "Groups orders by status.", SampleCommands.AggregateSummaryAsync),
            new("Aggregate", "Aggregate lookup", "Runs a lookup against a sample customers collection.", SampleCommands.AggregateLookupAsync),
            new("Aggregate", "Explain aggregate", "Runs an explain command for an aggregate pipeline.", SampleCommands.ExplainAggregateAsync),

            new("Advanced", "Transaction", "Runs a transaction if the server topology supports it.", SampleCommands.TransactionAsync),
            new("Advanced", "Change stream", "Opens a change stream and captures one generated change.", SampleCommands.ChangeStreamAsync),
            new("Advanced", "GridFS metadata style query", "Runs a representative query against fs.files.", SampleCommands.GridFsMetadataQueryAsync),

            new("Admin", "Ping", "Runs the ping command.", SampleCommands.PingAsync),
            new("Admin", "Build info", "Runs buildInfo.", SampleCommands.BuildInfoAsync),
            new("Admin", "Server status", "Runs serverStatus.", SampleCommands.ServerStatusAsync),
            new("Admin", "Connection status", "Runs connectionStatus.", SampleCommands.ConnectionStatusAsync),
            new("Admin", "List databases", "Lists databases.", SampleCommands.ListDatabasesAsync),
            new("Admin", "List collections", "Lists collections in the sample database.", SampleCommands.ListCollectionsAsync),
            new("Admin", "Database stats", "Runs dbStats.", SampleCommands.DatabaseStatsAsync),
            new("Admin", "Collection stats", "Runs collStats.", SampleCommands.CollectionStatsAsync),
            new("Admin", "Current op", "Runs currentOp.", SampleCommands.CurrentOpAsync),
            new("Admin", "Raw command JSON", "Runs any Mongo command JSON you type against the sample database.", SampleCommands.RawCommandAsync),

            new("Exit", "Quit", "Stops the relay and exits.", _ => Task.FromResult<CommandResult>(new TextResult("Bye.")), IsExit: true)
        ];
    }
}
