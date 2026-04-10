using Avalonia.Media;
using Avalonia.Threading;

namespace EFCore.Profiler.Viewer;

public partial class MainWindow
{
    private void SetStatus(string message, StatusKind statusKind)
    {
        void Apply()
        {
            var sequence = Interlocked.Increment(ref _statusSequence);
            var unixTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timestamped = $"[{unixTimeMs} #{sequence}] {message}";
            StatusBarLevel.Text = statusKind switch
            {
                StatusKind.Error => "ERROR",
                StatusKind.Warning => "WARN",
                _ => "INFO"
            };
            StatusBarLevel.Foreground = statusKind switch
            {
                StatusKind.Error => Brushes.IndianRed,
                StatusKind.Warning => Brushes.DarkOrange,
                _ => Brushes.SteelBlue
            };
            StatusBarMessage.Text = timestamped;
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private static string FormatException(Exception? exception)
    {
        if (exception is null)
            return "Unknown error.";

        var message = exception.Message ?? string.Empty;
        var firstLine = message.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
            return exception.GetType().Name;

        return firstLine.Length <= 180 ? firstLine : $"{firstLine[..180]}...";
    }

    private void UpdateEfSummary()
    {
        SummaryUniqueSqlText.Text = $"Unique SQL                 {_grpcAllEvents.Select(GetGroupingKey).Distinct(StringComparer.Ordinal).Count()}";
        SummaryWarningsText.Text = $"Warnings                   {_grpcAllEvents.Count(row => row.HasWarning)}";
        SummaryFailuresText.Text = $"Failed Events              {_grpcAllEvents.Count(row => row.HasFailure)}";
        SummaryTablesText.Text = $"Tables                     {_grpcAllEvents.Select(row => row.TableName).Where(HasValue).Distinct(StringComparer.OrdinalIgnoreCase).Count()}";
        SummaryDbContextsText.Text = $"DbContexts                 {_grpcAllEvents.Select(row => row.DbContextId).Where(HasValue).Distinct(StringComparer.OrdinalIgnoreCase).Count()}";
    }

    private static bool HasValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value != "-";
    }

    private class GrpcRawEventRow
    {
        public long Sequence { get; set; }
        public long UnixTimeMs { get; set; }
        public string CommandName { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string DbContextId { get; set; } = string.Empty;
        public string ServerEndpoint { get; set; } = string.Empty;
        public int? ResultCount { get; set; }
        public double DurationMs { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ShortQuery { get; set; } = string.Empty;
        public string FullQuery { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
        public string OperationId { get; set; } = string.Empty;
        public string TraceId { get; set; } = string.Empty;
        public string SpanId { get; set; } = string.Empty;
        public string Fingerprint { get; set; } = string.Empty;
        public string WarningSummary { get; set; } = string.Empty;
        public string WarningStatus { get; set; } = string.Empty;
        public string WinningPlanSummary { get; set; } = string.Empty;
        public string ExecutionPlanXml { get; set; } = string.Empty;

        public string ResultCountDisplay => ResultCount?.ToString() ?? "-";
        public string DurationDisplay => $"{DurationMs:F2} ms";
        public string LastSeenDisplay => UnixTimeMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(UnixTimeMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff")
            : "-";
        public bool HasWarning => !string.IsNullOrWhiteSpace(WarningSummary);
        public bool HasFailure => string.Equals(Status, "failed", StringComparison.OrdinalIgnoreCase);
        public string FingerprintDisplay => DisplayOrDash(Fingerprint);
        public string WarningSummaryDisplay => DisplayOrDash(WarningSummary);
    }

    private sealed class GrpcGridRow : GrpcRawEventRow
    {
        public string GroupKey { get; set; } = string.Empty;
        public int QueryCount { get; set; }
        public double AvgDurationMs { get; set; }
        public string AvgDurationDisplay => $"{AvgDurationMs:F2} ms";
    }

    private enum StatusKind
    {
        Info,
        Warning,
        Error
    }
}
