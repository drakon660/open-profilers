using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Grpc.Net.Client;

namespace Mongo.Profiler.Viewer;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<GrpcGridRow> _grpcRows = [];
    private readonly List<GrpcGridRow> _grpcAllGridRows = [];
    private readonly List<GrpcRawEventRow> _grpcAllEvents = [];
    private readonly ObservableCollection<ProfileGridRow> _profileRows = [];
    private readonly ObservableCollection<DataDetailRow> _dataDetailsRows = [];
    private readonly ConcurrentDictionary<string, byte> _directProfileSeenKeys = new(StringComparer.Ordinal);
    private bool _showingSampleRows;
    private long _statusSequence;
    private long _connectionStateVersion;
    private GrpcChannel? _channel;
    private CancellationTokenSource? _streamCancellation;
    private Task? _connectLoopTask;
    private CancellationTokenSource? _profileCancellation;
    private Task? _profileLoopTask;
    private bool _manualDisconnectRequested;
    private bool _autoReconnectEnabled;
    private readonly UnhandledExceptionEventHandler _unhandledExceptionHandler;
    private readonly EventHandler<UnobservedTaskExceptionEventArgs> _unobservedTaskExceptionHandler;

    public MainWindow()
    {
        _unhandledExceptionHandler = (_, args) =>
            SetStatus($"Unhandled error: {FormatException(args.ExceptionObject as Exception)}", StatusKind.Error);
        _unobservedTaskExceptionHandler = (_, args) =>
        {
            SetStatus($"Background task error: {FormatException(args.Exception)}", StatusKind.Error);
            args.SetObserved();
        };

        InitializeComponent();
        ApplyBuildVersionToTitle();
        GrpcEventsGrid.ItemsSource = _grpcRows;
        ProfileEventsGrid.ItemsSource = _profileRows;
        DataDetailsGrid.ItemsSource = _dataDetailsRows;
        AppDomain.CurrentDomain.UnhandledException += _unhandledExceptionHandler;
        TaskScheduler.UnobservedTaskException += _unobservedTaskExceptionHandler;
        SetStatus("Ready.", StatusKind.Info);
        Opened += (_, _) =>
        {
            StartSelectedMode(manualRequest: false);
            _ = CheckForViewerUpdateAvailabilityAsync(manualRequest: false);
        };
    }

    private void ApplyBuildVersionToTitle()
    {
        ApplyBuildVersionToTitle(ThisAssembly.NuGetPackageVersion);
    }

    private void AddSampleRows()
    {
        ClearRows();
        var now = DateTimeOffset.UtcNow;

        var samples = new (string CommandName, string Collection, string Server, string Status, string FullQuery, int? ResultCount, double DurationMs, string PlanSummary)[]
        {
            ("find",      "orders",    "localhost:27017",  "success", "{ \"find\" : \"orders\", \"filter\" : { \"status\" : \"open\" }, \"limit\" : 20 }",                                             20,  11.80, "IXSCAN { status: 1 }"),
            ("find",      "orders",    "localhost:27017",  "success", "{ \"find\" : \"orders\", \"filter\" : { \"customerId\" : 42 }, \"sort\" : { \"createdAt\" : -1 } }",                             12,   8.73, "IXSCAN { customerId: 1 }"),
            ("find",      "customers", "replica-1:27017",  "success", "{ \"find\" : \"customers\", \"filter\" : { \"email\" : \"alice@example.com\" } }",                                                1,   2.14, "IXSCAN { email: 1 }"),
            ("find",      "products",  "replica-2:27017",  "success", "{ \"find\" : \"products\", \"filter\" : { \"price\" : { \"$gt\" : 100 } }, \"limit\" : 50 }",                                    50, 142.80, "COLLSCAN"),
            ("aggregate", "orders",    "localhost:27017",  "success", "{ \"aggregate\" : \"orders\", \"pipeline\" : [ { \"$match\" : { \"status\" : \"shipped\" } }, { \"$group\" : { \"_id\" : \"$region\" } } ] }", 8,  58.21, "IXSCAN { status: 1 }"),
            ("aggregate", "events",    "replica-1:27017",  "success", "{ \"aggregate\" : \"events\", \"pipeline\" : [ { \"$match\" : { \"type\" : \"login\" } }, { \"$count\" : \"total\" } ] }",       1,  34.50, "IXSCAN { type: 1 }"),
            ("aggregate", "sessions",  "replica-2:27017",  "failed",  "{ \"aggregate\" : \"sessions\", \"pipeline\" : [ { \"$unwind\" : \"$events\" } ] }",                                            null, 912.33, "COLLSCAN"),
            ("insert",    "orders",    "localhost:27017",  "success", "{ \"insert\" : \"orders\", \"documents\" : [ { \"_id\" : 1001, \"status\" : \"new\" } ] }",                                       1,   4.02, ""),
            ("insert",    "logs",      "replica-1:27017",  "success", "{ \"insert\" : \"logs\", \"documents\" : [ { \"level\" : \"info\", \"message\" : \"ping\" } ] }",                                 1,   1.75, ""),
            ("update",    "orders",    "localhost:27017",  "success", "{ \"update\" : \"orders\", \"updates\" : [ { \"q\" : { \"_id\" : 1001 }, \"u\" : { \"$set\" : { \"status\" : \"shipped\" } } } ] }",  1,   3.48, "IXSCAN { _id: 1 }"),
            ("update",    "inventory", "replica-1:27017",  "failed",  "{ \"update\" : \"inventory\", \"updates\" : [ { \"q\" : { \"sku\" : \"ABC\" }, \"u\" : { \"$inc\" : { \"qty\" : -1 } } } ] }",     0,  15.92, "COLLSCAN"),
            ("delete",    "sessions",  "localhost:27017",  "success", "{ \"delete\" : \"sessions\", \"deletes\" : [ { \"q\" : { \"expiresAt\" : { \"$lt\" : \"2026-01-01\" } }, \"limit\" : 0 } ] }",   17,  22.10, "IXSCAN { expiresAt: 1 }"),
            ("count",     "orders",    "replica-2:27017",  "success", "{ \"count\" : \"orders\", \"query\" : { \"status\" : \"open\" } }",                                                              128,  5.60, "IXSCAN { status: 1 }"),
            ("count",     "customers", "localhost:27017",  "success", "{ \"count\" : \"customers\", \"query\" : {} }",                                                                               5042, 210.40, "COLLSCAN"),
            ("distinct",  "orders",    "replica-1:27017",  "success", "{ \"distinct\" : \"orders\", \"key\" : \"region\" }",                                                                             4,  18.03, "IXSCAN { region: 1 }"),
            ("find",      "orders",    "localhost:27017",  "failed",  "{ \"find\" : \"orders\", \"filter\" : { \"$where\" : \"this.total > 100\" } }",                                                null, 450.12, "COLLSCAN")
        };

        for (var index = 0; index < samples.Length; index++)
        {
            var sample = samples[index];
            var timestamp = now.AddMilliseconds(-index * 250);
            var fullQuery = sample.FullQuery;

            _grpcAllGridRows.Add(new GrpcGridRow
            {
                GroupKey = $"sample|{sample.CommandName}-{sample.Collection}-{index}",
                UnixTimeMs = timestamp.ToUnixTimeMilliseconds(),
                QueryCount = (index % 5) + 1,
                AvgDurationMs = sample.DurationMs,
                CommandName = sample.CommandName,
                SessionId = $"sample-session-{(index % 3) + 1}",
                ServerEndpoint = sample.Server,
                ResultCount = sample.ResultCount,
                DurationMs = sample.DurationMs,
                Status = sample.Status,
                ShortQuery = BuildShortQuery(fullQuery),
                FullQuery = fullQuery,
                OriginalCommand = fullQuery,
                OperationId = $"sample-op-{index:D3}",
                Fingerprint = $"sample-{sample.CommandName}-{sample.Collection}",
                WinningPlanSummary = sample.PlanSummary,
                Error = sample.Status == "failed" ? "sample error" : string.Empty
            });

            _profileRows.Add(new ProfileGridRow
            {
                TimestampUtc = timestamp,
                TsRaw = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                AppName = index % 2 == 0 ? "sample-api" : "sample-worker",
                Client = $"127.0.0.1:{50100 + index}",
                CommandDocument = fullQuery,
                DocsExamined = sample.ResultCount.HasValue ? sample.ResultCount.Value * 2 : 0,
                NReturned = sample.ResultCount ?? 0,
                Op = sample.CommandName == "find" || sample.CommandName == "aggregate" ? "query" : "command",
                CommandName = sample.CommandName,
                ServerEndpoint = sample.Server,
                DurationMs = sample.DurationMs,
                Status = sample.Status,
                Error = sample.Status == "failed" ? "sample error" : string.Empty
            });
        }

        ApplyGrpcFilter();
        if (_grpcRows.Count > 0)
            GrpcEventsGrid.SelectedItem = _grpcRows[0];
        if (_profileRows.Count > 0)
            ProfileEventsGrid.SelectedItem = _profileRows[0];
        _showingSampleRows = true;
        SetStatus($"Sample grid data loaded ({_grpcAllGridRows.Count} rows).", StatusKind.Info);
    }

    private void ApplyGrpcFilter()
    {
        var commandFilter = FilterCommandBox?.Text?.Trim() ?? string.Empty;
        var serverFilter = FilterServerBox?.Text?.Trim() ?? string.Empty;
        var statusFilter = FilterStatusBox?.Text?.Trim() ?? string.Empty;
        var queryFilter = FilterQueryBox?.Text?.Trim() ?? string.Empty;

        var previousSelection = GrpcEventsGrid.SelectedItem as GrpcGridRow;
        _grpcRows.Clear();
        foreach (var row in _grpcAllGridRows)
        {
            if (!MatchesFilter(row.CommandName, commandFilter)) continue;
            if (!MatchesFilter(row.ServerEndpoint, serverFilter)) continue;
            if (!MatchesFilter(row.Status, statusFilter)) continue;
            if (!MatchesFilter(row.FullQuery, queryFilter) && !MatchesFilter(row.ShortQuery, queryFilter))
                continue;
            _grpcRows.Add(row);
        }

        if (previousSelection is not null && _grpcRows.Contains(previousSelection))
            GrpcEventsGrid.SelectedItem = previousSelection;
    }

    private static bool MatchesFilter(string? value, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;
        return !string.IsNullOrEmpty(value)
            && value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void FilterTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyGrpcFilter();
    }

    private void FilterClear_Click(object? sender, RoutedEventArgs e)
    {
        FilterCommandBox.Text = string.Empty;
        FilterServerBox.Text = string.Empty;
        FilterStatusBox.Text = string.Empty;
        FilterQueryBox.Text = string.Empty;
        ApplyGrpcFilter();
    }

    private async void ExportGrpc_Click(object? sender, RoutedEventArgs e)
    {
        if (_grpcRows.Count == 0)
        {
            SetStatus("No rows to export.", StatusKind.Warning);
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(desktopPath);
        var suggestedName = $"grpc-events-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json";

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export gRPC events",
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            ShowOverwritePrompt = true,
            SuggestedStartLocation = startLocation,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } }
            }
        });

        if (file is null)
            return;

        try
        {
            var payload = _grpcRows.Select(row => new
            {
                unixTimeMs = row.UnixTimeMs,
                queryCount = row.QueryCount,
                avgDurationMs = row.AvgDurationMs,
                durationMs = row.DurationMs,
                status = row.Status,
                commandName = row.CommandName,
                sessionId = row.SessionId,
                serverEndpoint = row.ServerEndpoint,
                operationId = row.OperationId,
                resultCount = row.ResultCount,
                errorCode = row.ErrorCode,
                errorCodeName = row.ErrorCodeName,
                error = row.Error,
                fingerprint = row.Fingerprint,
                winningPlanSummary = row.WinningPlanSummary,
                shortQuery = row.ShortQuery,
                fullQuery = row.FullQuery,
                originalCommand = row.OriginalCommand
            }).ToList();

            await using var stream = await file.OpenWriteAsync();
            await JsonSerializer.SerializeAsync(stream, payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            SetStatus($"Exported {payload.Count} rows to {file.Name}.", StatusKind.Info);
        }
        catch (Exception ex)
        {
            SetStatus($"Export failed: {ex.Message}", StatusKind.Error);
        }
    }

    private void LoadSampleRows_Click(object? sender, RoutedEventArgs e)
    {
        AddSampleRows();
    }

    private void RemoveSampleRows()
    {
        if (!_showingSampleRows)
            return;

        _grpcRows.Clear();
        _profileRows.Clear();
        ClearDetailsPanel();
        _showingSampleRows = false;
    }

    private void ApplyBuildVersionToTitle(string? rawVersion)
    {
        var buildVersion = string.IsNullOrWhiteSpace(rawVersion) ? ThisAssembly.NuGetPackageVersion : rawVersion;
        var titleText = $"Mongo Profiler (Avalonia) build {buildVersion}";
        AppTitleText.Text = titleText;
        Title = titleText;
    }

    protected override void OnClosed(EventArgs e)
    {
        _manualDisconnectRequested = true;
        AppDomain.CurrentDomain.UnhandledException -= _unhandledExceptionHandler;
        TaskScheduler.UnobservedTaskException -= _unobservedTaskExceptionHandler;
        _streamCancellation?.Cancel();
        _streamCancellation?.Dispose();
        _profileCancellation?.Cancel();
        _profileCancellation?.Dispose();
        _channel?.Dispose();
        base.OnClosed(e);
    }

    private void TitleBarDragArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void TitleBarDragArea_DoubleTapped(object? sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaxRestore_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
