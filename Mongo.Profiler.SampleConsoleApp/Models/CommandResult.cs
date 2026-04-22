using MongoDB.Bson;

namespace Mongo.Profiler.SampleConsoleApp.Models;

internal abstract record CommandResult;

internal sealed record TextResult(string Value) : CommandResult;

internal sealed record DocumentResult(BsonDocument Value) : CommandResult;

internal sealed record DocumentsResult(IReadOnlyList<BsonDocument> Values) : CommandResult;

internal sealed record JsonResult(BsonValue Value) : CommandResult;
