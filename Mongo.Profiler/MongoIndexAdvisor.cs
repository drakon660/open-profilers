using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Mongo.Profiler;

internal sealed record IndexAdvice(
    string Status,
    string Reason,
    long? DocsExamined,
    long? KeysExamined,
    long? NReturned,
    string WinningPlanSummary)
{
    public static readonly IndexAdvice None = new(string.Empty, string.Empty, null, null, null, string.Empty);
}

internal sealed class MongoIndexAdvisor
{
    public const string AdvisorComment = "mongo-profiler-index-advisor";

    private readonly IMongoClient _client;
    private readonly MongoProfilerIndexAdvisorOptions _options;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAnalysisByFingerprint = new(StringComparer.Ordinal);
    private DateTimeOffset _suspendUntilUtc;

    private MongoIndexAdvisor(IMongoClient client, MongoProfilerIndexAdvisorOptions options, ILogger? logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public static MongoIndexAdvisor? Create(
        MongoClientSettings settings,
        MongoProfilerIndexAdvisorOptions options,
        ILogger? logger)
    {
        if (!options.Enabled)
            return null;

        try
        {
            var advisorSettings = settings.Clone();
            advisorSettings.ClusterConfigurator = null;
            advisorSettings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(Math.Clamp(options.ExplainTimeoutMs, 250, 5_000));
            var client = new MongoClient(advisorSettings);
            return new MongoIndexAdvisor(client, options, logger);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Mongo index advisor initialization failed.");
            return null;
        }
    }

    public IndexAdvice Analyze(CommandEnvelope envelope, string commandName, TimeSpan duration)
    {
        if (!ShouldAnalyze(commandName, duration, envelope.QueryFingerprint))
            return IndexAdvice.None;

        try
        {
            var explain = BuildExplainCommand(envelope.OriginalCommand);
            var database = _client.GetDatabase(envelope.DatabaseName);
            using var timeoutSource = new CancellationTokenSource(_options.ExplainTimeoutMs);
            var response = database.RunCommand<BsonDocument>(explain, cancellationToken: timeoutSource.Token);
            return BuildAdviceFromExplain(response);
        }
        catch (OperationCanceledException)
        {
            SuspendAnalysisFor(TimeSpan.FromSeconds(10));
            return new IndexAdvice("analysis_timeout", "explain timed out", null, null, null, string.Empty);
        }
        catch (TimeoutException timeoutException)
        {
            SuspendAnalysisFor(TimeSpan.FromSeconds(15));
            return new IndexAdvice("analysis_failed", timeoutException.Message, null, null, null, string.Empty);
        }
        catch (MongoConnectionException connectionException)
        {
            SuspendAnalysisFor(TimeSpan.FromSeconds(15));
            return new IndexAdvice("analysis_failed", connectionException.Message, null, null, null, string.Empty);
        }
        catch (Exception exception)
        {
            _logger?.LogDebug(exception, "Mongo index advisor analysis failed.");
            return new IndexAdvice("analysis_failed", exception.Message, null, null, null, string.Empty);
        }
    }

    private bool ShouldAnalyze(string commandName, TimeSpan duration, string queryFingerprint)
    {
        if (commandName is not ("find" or "aggregate"))
            return false;

        if (duration.TotalMilliseconds < _options.SlowQueryThresholdMs)
            return false;

        if (string.IsNullOrWhiteSpace(queryFingerprint))
            return false;

        var now = DateTimeOffset.UtcNow;
        if (now < _suspendUntilUtc)
            return false;

        if (_lastAnalysisByFingerprint.TryGetValue(queryFingerprint, out var lastAnalysisUtc))
        {
            var minInterval = TimeSpan.FromMinutes(1d / Math.Max(1, _options.MaxAnalysesPerFingerprintPerMinute));
            if (now - lastAnalysisUtc < minInterval)
                return false;
        }

        _lastAnalysisByFingerprint[queryFingerprint] = now;
        return true;
    }

    private void SuspendAnalysisFor(TimeSpan duration)
    {
        var next = DateTimeOffset.UtcNow.Add(duration);
        if (next > _suspendUntilUtc)
            _suspendUntilUtc = next;
    }

    private static BsonDocument BuildExplainCommand(BsonDocument originalCommand)
    {
        var explainableCommand = originalCommand.DeepClone().AsBsonDocument;
        explainableCommand.Remove("$db");
        explainableCommand.Remove("lsid");
        explainableCommand.Remove("$clusterTime");
        explainableCommand.Remove("$readPreference");
        explainableCommand["comment"] = AdvisorComment;

        return new BsonDocument
        {
            { "explain", explainableCommand },
            { "verbosity", "executionStats" },
            { "comment", AdvisorComment }
        };
    }

    private IndexAdvice BuildAdviceFromExplain(BsonDocument explain)
    {
        var docsExamined = ReadLong(explain, "executionStats.totalDocsExamined");
        var keysExamined = ReadLong(explain, "executionStats.totalKeysExamined");
        var nReturned = ReadLong(explain, "executionStats.nReturned");
        var hasCollectionScan = ContainsStage(explain, "COLLSCAN");
        var hasIndexScan = ContainsStage(explain, "IXSCAN");
        var winningPlanSummary = hasCollectionScan ? "COLLSCAN" : hasIndexScan ? "IXSCAN" : "UNKNOWN";

        if (hasCollectionScan && docsExamined.HasValue && docsExamined.Value >= _options.MinDocsExaminedForWarning)
        {
            return new IndexAdvice(
                "possible_missing_index",
                "collection scan with high documents examined",
                docsExamined,
                keysExamined,
                nReturned,
                winningPlanSummary);
        }

        if (docsExamined.HasValue && nReturned.HasValue)
        {
            var docsToReturnThreshold = Math.Max(50L, nReturned.Value * 20L);
            if (docsExamined.Value > docsToReturnThreshold && keysExamined.GetValueOrDefault() == 0)
            {
                return new IndexAdvice(
                    "possible_missing_index",
                    "many documents examined compared to rows returned",
                    docsExamined,
                    keysExamined,
                    nReturned,
                    winningPlanSummary);
            }
        }

        return new IndexAdvice(
            "ok",
            hasIndexScan ? "index scan observed" : "no obvious index issue",
            docsExamined,
            keysExamined,
            nReturned,
            winningPlanSummary);
    }

    private static bool ContainsStage(BsonValue value, string stageName)
    {
        if (value.BsonType == BsonType.Document)
        {
            var document = value.AsBsonDocument;
            if (document.TryGetValue("stage", out var stage) &&
                stage.BsonType == BsonType.String &&
                string.Equals(stage.AsString, stageName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var element in document)
            {
                if (ContainsStage(element.Value, stageName))
                    return true;
            }
        }
        else if (value.BsonType == BsonType.Array)
        {
            foreach (var item in value.AsBsonArray)
            {
                if (ContainsStage(item, stageName))
                    return true;
            }
        }

        return false;
    }

    private static long? ReadLong(BsonDocument source, string dottedPath)
    {
        var current = (BsonValue)source;
        foreach (var segment in dottedPath.Split('.'))
        {
            if (current.BsonType != BsonType.Document || !current.AsBsonDocument.TryGetValue(segment, out current))
                return null;
        }

        return current.BsonType switch
        {
            BsonType.Int32 => current.AsInt32,
            BsonType.Int64 => current.AsInt64,
            BsonType.Double => (long)current.AsDouble,
            BsonType.Decimal128 => (long)current.AsDecimal128,
            _ => null
        };
    }
}
