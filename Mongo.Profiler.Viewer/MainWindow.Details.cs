using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Mongo.Profiler;

namespace Mongo.Profiler.Viewer;

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
        panel.Children.Add(CreateOptionButton("Copy Query", () => CopyQuery_Click(null, new RoutedEventArgs())));
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
        ClearRows();
        SetStatus("Cleared captured events.", StatusKind.Info);
    }

    private void ClearRows()
    {
        _grpcAllEvents.Clear();
        _grpcAllGridRows.Clear();
        _profileRows.Clear();
        _directProfileSeenKeys.Clear();
        _grpcRows.Clear();
        _showingSampleRows = false;
        ClearDetailsPanel();
    }

    private void GrpcEventsGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (GrpcEventsGrid.SelectedItem is not GrpcGridRow row)
        {
            ClearDetailsPanel();
            return;
        }

        QueryCommandBox.Text = PrettifyForDisplay(row.FullQuery);
        RawCommandBox.Text = row.OriginalCommand ?? string.Empty;
        ReplaceDataRows(BuildDataRows(row));
    }

    private void ProfileEventsGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProfileEventsGrid.SelectedItem is not ProfileGridRow row)
        {
            ClearDetailsPanel();
            return;
        }

        QueryCommandBox.Text = PrettifyForDisplay(row.CommandDocument);
        RawCommandBox.Text = row.CommandDocument ?? string.Empty;
        ReplaceDataRows(BuildDataRows(row));
    }

    private void CopyQuery_Click(object? sender, RoutedEventArgs e)
    {
        var grpcQuery = (GrpcEventsGrid.SelectedItem as GrpcGridRow)?.FullQuery;
        var profileQuery = (ProfileEventsGrid.SelectedItem as ProfileGridRow)?.CommandDocument;
        var query = !string.IsNullOrWhiteSpace(grpcQuery) ? grpcQuery : profileQuery;
        if (string.IsNullOrWhiteSpace(query))
        {
            SetStatus("No query selected to copy.", StatusKind.Warning);
            return;
        }

        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(query);
        SetStatus("Query copied to clipboard.", StatusKind.Info);
    }

    private async void CopyQueryCommandValue_Click(object? sender, RoutedEventArgs e)
    {
        var queryCommandRow = _dataDetailsRows.FirstOrDefault(x =>
            string.Equals(x.Info, "query_command", StringComparison.OrdinalIgnoreCase));
        if (queryCommandRow is null || string.IsNullOrWhiteSpace(queryCommandRow.Value) || queryCommandRow.Value == "-")
        {
            SetStatus("No query_command value available to copy.", StatusKind.Warning);
            return;
        }

        await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(queryCommandRow.Value) ?? Task.CompletedTask);
        SetStatus("query_command value copied to clipboard.", StatusKind.Info);
    }

    private void ClearDetailsPanel()
    {
        QueryCommandBox.Text = string.Empty;
        RawCommandBox.Text = string.Empty;
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
        yield return new DataDetailRow("query_command", string.IsNullOrWhiteSpace(row.FullQuery) ? "-" : row.FullQuery);
        yield return new DataDetailRow("original_command", string.IsNullOrWhiteSpace(row.OriginalCommand) ? "-" : row.OriginalCommand);
        yield return new DataDetailRow("unix_time_ms", row.UnixTimeMsDisplay);
        yield return new DataDetailRow("query_count", row.QueryCount.ToString());
        yield return new DataDetailRow("avg_duration", row.AvgDurationDisplay);
        yield return new DataDetailRow("command", DisplayOrDash(row.CommandName));
        yield return new DataDetailRow("server", DisplayOrDash(row.ServerEndpoint));
        yield return new DataDetailRow("duration", row.DurationDisplay);
        yield return new DataDetailRow("result_count", row.ResultCountDisplay);
        yield return new DataDetailRow("session", DisplayOrDash(row.SessionId));
        yield return new DataDetailRow("operation_id", DisplayOrDash(row.OperationId));
        yield return new DataDetailRow("error_code", row.ErrorCodeDisplay);
        yield return new DataDetailRow("error_name", DisplayOrDash(row.ErrorCodeName));
        yield return new DataDetailRow("status", row.Status);
        if (!string.IsNullOrWhiteSpace(row.Error))
            yield return new DataDetailRow("error_message", row.Error);
        yield return new DataDetailRow("fingerprint", DisplayOrDash(row.Fingerprint));
        if (!string.IsNullOrWhiteSpace(row.WinningPlanSummary))
            yield return new DataDetailRow("execution_plan_summary", row.WinningPlanSummary);
        if (!string.IsNullOrWhiteSpace(row.ExecutionPlanXml))
            yield return new DataDetailRow("execution_plan_xml", row.ExecutionPlanXml);
    }

    private static IEnumerable<DataDetailRow> BuildDataRows(ProfileGridRow row)
    {
        yield return new DataDetailRow("query_command", string.IsNullOrWhiteSpace(row.CommandDocument) ? "-" : row.CommandDocument);
        yield return new DataDetailRow("ts", row.TsDisplay);
        yield return new DataDetailRow("app_name", row.AppNameDisplay);
        yield return new DataDetailRow("client", row.ClientDisplay);
        yield return new DataDetailRow("op", row.OpDisplay);
        yield return new DataDetailRow("docs_examined", row.DocsExaminedDisplay);
        yield return new DataDetailRow("nreturned", row.NReturnedDisplay);
        yield return new DataDetailRow("command", DisplayOrDash(row.CommandName));
        yield return new DataDetailRow("server", DisplayOrDash(row.ServerEndpoint));
        yield return new DataDetailRow("duration", $"{row.DurationMs:F2} ms");
        yield return new DataDetailRow("status", row.Status);
        if (!string.IsNullOrWhiteSpace(row.Error))
            yield return new DataDetailRow("error_message", row.Error);
    }

    private static string BuildShortQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "<empty>";

        var singleLine = query.Replace(Environment.NewLine, " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return singleLine.Length <= 95 ? singleLine : $"{singleLine[..95]}...";
    }

    private static string DisplayOrDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static string PrettifyForDisplay(string? rawQueryOrCommand)
    {
        if (string.IsNullOrWhiteSpace(rawQueryOrCommand))
            return string.Empty;

        try
        {
            var pretty = MongoQueryPrettier.Prettify(rawQueryOrCommand);
            return string.IsNullOrWhiteSpace(pretty) ? rawQueryOrCommand : pretty;
        }
        catch
        {
            // Display should never fail because formatting fails.
            return rawQueryOrCommand;
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
