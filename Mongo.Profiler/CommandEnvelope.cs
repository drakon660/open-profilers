using MongoDB.Bson;

namespace Mongo.Profiler;

internal sealed record CommandEnvelope(
    string Query,
    string QueryFingerprint,
    string DatabaseName,
    string CollectionName,
    string SessionId,
    string ServerEndpoint,
    string OperationId,
    string ReadPreference,
    string ReadConcern,
    string WriteConcern,
    int? MaxTimeMs,
    bool? AllowDiskUse,
    int? CommandSizeBytes,
    BsonDocument OriginalCommand,
    string TraceId,
    string SpanId);
