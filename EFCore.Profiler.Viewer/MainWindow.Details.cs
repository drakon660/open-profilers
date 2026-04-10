using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace EFCore.Profiler.Viewer;

public partial class MainWindow
{
    private async void Options_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Options",
            Width = 280,
            Height = 300,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 10
        };
        panel.Children.Add(CreateOptionButton("Connect", () => Connect_Click(null, new RoutedEventArgs())));
        panel.Children.Add(CreateOptionButton("Disconnect", () => Disconnect_Click(null, new RoutedEventArgs())));
        panel.Children.Add(CreateOptionButton("Clear", () => Clear_Click(null, new RoutedEventArgs())));
        panel.Children.Add(CreateOptionButton("Copy SQL", () => CopyQuery_Click(null, new RoutedEventArgs())));
        panel.Children.Add(CreateOptionButton("Check Updates", () => _ = CheckForViewerUpdateAvailabilityAsync(manualRequest: true)));
        panel.Children.Add(CreateOptionButton("Exit App", Close));
        panel.Children.Add(CreateOptionButton("Close Dialog", () => dialog.Close()));
        dialog.Content = panel;

        await dialog.ShowDialog(this);
    }

    private static Button CreateOptionButton(string caption, Action action)
    {
        var button = new Button
        {
            Content = caption,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void MenuExit_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        _grpcAllEvents.Clear();
        _grpcRows.Clear();
        _grpcEventSequence = 0;
        UpdateEfSummary();
        ClearDetailsPanel();
        SetStatus("Cleared captured EF Core events.", StatusKind.Info);
    }

    private void GrpcEventsGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (GrpcEventsGrid.SelectedItem is not GrpcGridRow row)
        {
            ClearDetailsPanel();
            return;
        }

        QueryCommandBox.Text = PrettifyForDisplay(row.FullQuery);
        ReplaceDataRows(BuildDataRows(row));
    }

    private async void CopyQuery_Click(object? sender, RoutedEventArgs e)
    {
        var query = (GrpcEventsGrid.SelectedItem as GrpcGridRow)?.FullQuery;
        if (string.IsNullOrWhiteSpace(query))
        {
            SetStatus("No SQL selected to copy.", StatusKind.Warning);
            return;
        }

        await CopyTextToClipboardAsync(query, "SQL copied to clipboard.");
    }

    private async void CopyQueryCommandValue_Click(object? sender, RoutedEventArgs e)
    {
        var query = (GrpcEventsGrid.SelectedItem as GrpcGridRow)?.FullQuery;
        if (string.IsNullOrWhiteSpace(query))
        {
            SetStatus("No sql value available to copy.", StatusKind.Warning);
            return;
        }

        await CopyTextToClipboardAsync(query, "sql value copied to clipboard.");
    }

    private void ClearDetailsPanel()
    {
        QueryCommandBox.Text = string.Empty;
        _dataDetailsRows.Clear();
    }

    private void ReplaceDataRows(IEnumerable<DataDetailRow> rows)
    {
        _dataDetailsRows.Clear();
        foreach (var row in rows)
            _dataDetailsRows.Add(row);
    }

    private static IEnumerable<DataDetailRow> BuildDataRows(GrpcGridRow row)
    {
        yield return new DataDetailRow("sql", DisplaySingleLineOrDash(row.FullQuery));
        yield return new DataDetailRow("last_seen", row.LastSeenDisplay);
        yield return new DataDetailRow("query_count", row.QueryCount.ToString());
        yield return new DataDetailRow("avg_duration", row.AvgDurationDisplay);
        yield return new DataDetailRow("duration", row.DurationDisplay);
        yield return new DataDetailRow("status", DisplayOrDash(row.Status));
        yield return new DataDetailRow("sql_command", DisplayOrDash(row.CommandName));
        yield return new DataDetailRow("database", DisplayOrDash(row.DatabaseName));
        yield return new DataDetailRow("table", DisplayOrDash(row.TableName));
        yield return new DataDetailRow("dbcontext_id", DisplayOrDash(row.DbContextId));
        yield return new DataDetailRow("request_id", DisplayOrDash(row.RequestId));
        yield return new DataDetailRow("operation_id", DisplayOrDash(row.OperationId));
        yield return new DataDetailRow("trace_id", DisplayOrDash(row.TraceId));
        yield return new DataDetailRow("span_id", DisplayOrDash(row.SpanId));
        yield return new DataDetailRow("result_count", row.ResultCountDisplay);
        yield return new DataDetailRow("fingerprint", row.FingerprintDisplay);
        yield return new DataDetailRow("warnings", row.WarningSummaryDisplay);
        yield return new DataDetailRow("warning_status", DisplayOrDash(row.WarningStatus));
        yield return new DataDetailRow("server", DisplayOrDash(row.ServerEndpoint));
        if (!string.IsNullOrWhiteSpace(row.Error))
            yield return new DataDetailRow("error_message", row.Error);
        if (!string.IsNullOrWhiteSpace(row.WinningPlanSummary))
            yield return new DataDetailRow("execution_plan_summary", row.WinningPlanSummary);
        if (!string.IsNullOrWhiteSpace(row.ExecutionPlanXml))
            yield return new DataDetailRow("execution_plan_xml", row.ExecutionPlanXml);
    }

    private static string BuildShortQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "<empty>";

        var singleLine = query.Replace(Environment.NewLine, " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return singleLine.Length <= 110 ? singleLine : $"{singleLine[..110]}...";
    }

    private static string DisplayOrDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static string DisplaySingleLineOrDash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "-";

        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static string PrettifyForDisplay(string? rawSql)
    {
        if (string.IsNullOrWhiteSpace(rawSql))
            return string.Empty;

        return rawSql.Replace("\r\n", "\n").Trim();
    }

    private async Task CopyTextToClipboardAsync(string text, string successMessage)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                SetStatus("Clipboard is unavailable.", StatusKind.Error);
                return;
            }

            await clipboard.SetTextAsync(text);
            SetStatus(successMessage, StatusKind.Info);
        }
        catch (Exception ex)
        {
            SetStatus($"Clipboard copy failed: {FormatException(ex)}", StatusKind.Error);
        }
    }

    private sealed class DataDetailRow
    {
        public DataDetailRow(string info, string value)
        {
            Info = info;
            Value = value;
        }

        public string Info { get; }
        public string Value { get; }
    }
}
