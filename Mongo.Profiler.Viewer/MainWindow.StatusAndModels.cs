using Avalonia.Media;
using Avalonia.Threading;

namespace Mongo.Profiler.Viewer;

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

    private class GrpcRawEventRow
    {
        public long UnixTimeMs { get; set; }
        public string CommandName { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string ServerEndpoint { get; set; } = string.Empty;
        public int? ResultCount { get; set; }
        public double DurationMs { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ShortQuery { get; set; } = string.Empty;
        public string FullQuery { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string OperationId { get; set; } = string.Empty;
        public int? ErrorCode { get; set; }
        public string ErrorCodeName { get; set; } = string.Empty;
        public string Fingerprint { get; set; } = string.Empty;
        public string WinningPlanSummary { get; set; } = string.Empty;
        public string ExecutionPlanXml { get; set; } = string.Empty;

        public string UnixTimeMsDisplay => UnixTimeMs > 0 ? UnixTimeMs.ToString() : "-";
        public string ResultCountDisplay => ResultCount?.ToString() ?? "-";
        public string DurationDisplay => $"{DurationMs:F2} ms";
        public string ErrorCodeDisplay => ErrorCode?.ToString() ?? "-";
    }

    private sealed class GrpcGridRow : GrpcRawEventRow
    {
        public string GroupKey { get; set; } = string.Empty;
        public int QueryCount { get; set; }
        public double AvgDurationMs { get; set; }
        public string AvgDurationDisplay => $"{AvgDurationMs:F2} ms";
    }

    private sealed class ProfileGridRow
    {
        public DateTimeOffset? TimestampUtc { get; set; }
        public string TsRaw { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public string Client { get; set; } = string.Empty;
        public string CommandDocument { get; set; } = string.Empty;
        public long? DocsExamined { get; set; }
        public long? NReturned { get; set; }
        public string Op { get; set; } = string.Empty;
        public string CommandName { get; set; } = string.Empty;
        public string ServerEndpoint { get; set; } = string.Empty;
        public double DurationMs { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;

        public string TsDisplay => string.IsNullOrWhiteSpace(TsRaw) ? "-" : TsRaw;
        public string TsDateTimeDisplay => TimestampUtc.HasValue
            ? TimestampUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff")
            : "-";
        public string AppNameDisplay => string.IsNullOrWhiteSpace(AppName) ? "-" : AppName;
        public string ClientDisplay => string.IsNullOrWhiteSpace(Client) ? "-" : Client;
        public string OpDisplay => string.IsNullOrWhiteSpace(Op) ? "-" : Op;
        public string DocsExaminedDisplay => DocsExamined?.ToString() ?? "-";
        public string NReturnedDisplay => NReturned?.ToString() ?? "-";
        public string CommandDocumentDisplay => string.IsNullOrWhiteSpace(CommandDocument) ? "-" : CommandDocument;
    }

    private enum StatusKind
    {
        Info,
        Warning,
        Error
    }
}
