using Grpc.Core;
using Mongo.Profiler;

namespace Mongo.Profiler.Grpc.Services;

public sealed class ProfilerStreamService : ProfilerStream.ProfilerStreamBase
{
    private readonly MongoProfilerEventChannelBroadcaster _channelBroadcaster;

    public ProfilerStreamService(MongoProfilerEventChannelBroadcaster channelBroadcaster)
    {
        _channelBroadcaster = channelBroadcaster;
    }

    public override async Task Subscribe(SubscribeRequest request, IServerStreamWriter<ProfilerEvent> responseStream,
        ServerCallContext context)
    {
        try
        {
            await foreach (var queryEvent in _channelBroadcaster.Subscribe(context.CancellationToken))
            {
                await responseStream.WriteAsync(Map(queryEvent));
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Client disconnected. Treat as a normal stream end.
        }
        catch (RpcException rpcException)
            when (rpcException.StatusCode == StatusCode.Cancelled && context.CancellationToken.IsCancellationRequested)
        {
            // Client disconnected. Treat as a normal stream end.
        }
    }

    private static ProfilerEvent Map(MongoProfilerQueryEvent queryEvent)
    {
        var result = new ProfilerEvent
        {
            SchemaVersion = queryEvent.SchemaVersion,
            EventId = queryEvent.EventId,
            UnixTimeMs = queryEvent.UnixTimeMs,
            CommandName = queryEvent.CommandName,
            DatabaseName = queryEvent.DatabaseName,
            CollectionName = queryEvent.CollectionName,
            Query = queryEvent.Query,
            DurationMs = queryEvent.DurationMs,
            Success = queryEvent.Success,
            ErrorMessage = queryEvent.ErrorMessage ?? string.Empty,
            RequestId = queryEvent.RequestId,
            TraceId = queryEvent.TraceId,
            SpanId = queryEvent.SpanId,
            SessionId = queryEvent.SessionId
        };

        if (!string.IsNullOrWhiteSpace(queryEvent.ServerEndpoint))
            result.ServerEndpoint = queryEvent.ServerEndpoint;
        if (!string.IsNullOrWhiteSpace(queryEvent.OperationId))
            result.OperationId = queryEvent.OperationId;
        if (!string.IsNullOrWhiteSpace(queryEvent.ErrorCodeName))
            result.ErrorCodeName = queryEvent.ErrorCodeName;
        if (!string.IsNullOrWhiteSpace(queryEvent.QueryFingerprint))
            result.QueryFingerprint = queryEvent.QueryFingerprint;
        if (!string.IsNullOrWhiteSpace(queryEvent.ReadPreference))
            result.ReadPreference = queryEvent.ReadPreference;
        if (!string.IsNullOrWhiteSpace(queryEvent.ReadConcern))
            result.ReadConcern = queryEvent.ReadConcern;
        if (!string.IsNullOrWhiteSpace(queryEvent.WriteConcern))
            result.WriteConcern = queryEvent.WriteConcern;
        if (!string.IsNullOrWhiteSpace(queryEvent.IndexAdviceStatus))
            result.IndexAdviceStatus = queryEvent.IndexAdviceStatus;
        if (!string.IsNullOrWhiteSpace(queryEvent.IndexAdviceReason))
            result.IndexAdviceReason = queryEvent.IndexAdviceReason;
        if (!string.IsNullOrWhiteSpace(queryEvent.WinningPlanSummary))
            result.WinningPlanSummary = queryEvent.WinningPlanSummary;
        if (!string.IsNullOrWhiteSpace(queryEvent.ExecutionPlanXml))
            result.ExecutionPlanXml = queryEvent.ExecutionPlanXml;
        if (!string.IsNullOrWhiteSpace(queryEvent.ApplicationName))
            result.ApplicationName = queryEvent.ApplicationName;
        if (!string.IsNullOrWhiteSpace(queryEvent.OriginalCommand))
            result.OriginalCommand = queryEvent.OriginalCommand;

        if (queryEvent.ResultCount.HasValue)
            result.ResultCount = queryEvent.ResultCount.Value;
        if (queryEvent.ErrorCode.HasValue)
            result.ErrorCode = queryEvent.ErrorCode.Value;
        if (queryEvent.CursorId.HasValue)
            result.CursorId = queryEvent.CursorId.Value;
        if (queryEvent.ReplySizeBytes.HasValue)
            result.ReplySizeBytes = queryEvent.ReplySizeBytes.Value;
        if (queryEvent.CommandSizeBytes.HasValue)
            result.CommandSizeBytes = queryEvent.CommandSizeBytes.Value;
        if (queryEvent.MaxTimeMs.HasValue)
            result.MaxTimeMs = queryEvent.MaxTimeMs.Value;
        if (queryEvent.AllowDiskUse.HasValue)
            result.AllowDiskUse = queryEvent.AllowDiskUse.Value;
        if (queryEvent.ExplainDocsExamined.HasValue)
            result.ExplainDocsExamined = queryEvent.ExplainDocsExamined.Value;
        if (queryEvent.ExplainKeysExamined.HasValue)
            result.ExplainKeysExamined = queryEvent.ExplainKeysExamined.Value;
        if (queryEvent.ExplainNReturned.HasValue)
            result.ExplainNReturned = queryEvent.ExplainNReturned.Value;
        result.ErrorLabels.AddRange(queryEvent.ErrorLabels);

        return result;
    }
}
