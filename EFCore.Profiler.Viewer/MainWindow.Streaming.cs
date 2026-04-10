using Avalonia.Interactivity;
using Avalonia.Threading;
using Grpc.Core;
using Grpc.Net.Client;
using Mongo.Profiler.Grpc;

namespace EFCore.Profiler.Viewer;

public partial class MainWindow
{
    private void Connect_Click(object? sender, RoutedEventArgs e)
    {
        StartSelectedMode(manualRequest: true);
    }

    private void AutoReconnectChanged(object? sender, RoutedEventArgs e)
    {
        _autoReconnectEnabled = AutoReconnectCheckBox.IsChecked == true;
        SetStatus(_autoReconnectEnabled
            ? "Auto reconnect enabled."
            : "Auto reconnect disabled.", StatusKind.Info);
    }

    private void StartSelectedMode(bool manualRequest)
    {
        if (!manualRequest && !_autoReconnectEnabled)
            return;

        StartReconnectLoop(manualRequest);
    }

    private void StartReconnectLoop(bool manualRequest)
    {
        if (_connectLoopTask is { IsCompleted: false })
        {
            SetStatus("Already connected or connecting.", StatusKind.Warning);
            return;
        }

        _manualDisconnectRequested = false;
        if (manualRequest)
            SetStatus("Connect requested.", StatusKind.Info);
        else
            SetStatus("Auto-connect enabled. Starting connection loop.", StatusKind.Info);
        _connectLoopTask = RunReconnectLoopAsync();
    }

    private async Task RunReconnectLoopAsync()
    {
        var attempt = 0;
        while (!_manualDisconnectRequested)
        {
            attempt++;
            var attemptVersion = Interlocked.Increment(ref _connectionStateVersion);
            _streamCancellation = new CancellationTokenSource();
            SetUiConnectedState(connecting: true, attemptVersion);

            var host = string.IsNullOrWhiteSpace(HostBox.Text) ? "localhost" : HostBox.Text.Trim();
            var port = int.TryParse(PortBox.Text, out var parsedPort) ? parsedPort : 5179;
            SetConnectionText("Connecting...", attemptVersion);
            SetStatus(
                $"Connecting to gRPC stream at {host}:{port} (attempt {attempt}, state_version={attemptVersion})...",
                StatusKind.Info);

            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            _channel = GrpcChannel.ForAddress($"http://{host}:{port}");
            var client = new ProfilerStream.ProfilerStreamClient(_channel);

            try
            {
                var call = client.Subscribe(new SubscribeRequest
                {
                    ClientName = "efcore-profiler-viewer",
                    SchemaVersion = "preview"
                }, cancellationToken: _streamCancellation.Token);

                SetConnectionText("Subscribed, waiting for ack/events...", attemptVersion);
                await call.ResponseHeadersAsync;

                SetConnectionText($"Connected to {host}:{port}", attemptVersion);
                SetStatus(
                    $"Connected to gRPC at {host}:{port} (attempt {attempt}, state_version={attemptVersion})",
                    StatusKind.Info);
                attempt = 0;

                await foreach (var profilerEvent in call.ResponseStream.ReadAllAsync(_streamCancellation.Token))
                    await Dispatcher.UIThread.InvokeAsync(() => PushEventToUi(profilerEvent));
            }
            catch (OperationCanceledException)
            {
                SetConnectionText("Disconnected", attemptVersion);
                SetStatus($"Disconnected (state_version={attemptVersion}).", StatusKind.Info);
            }
            catch (RpcException rpcException) when (rpcException.StatusCode == StatusCode.Cancelled)
            {
                SetConnectionText("Disconnected", attemptVersion);
                SetStatus($"Disconnected (state_version={attemptVersion}).", StatusKind.Info);
            }
            catch (RpcException rpcException)
            {
                SetConnectionText("Connection error", attemptVersion);
                SetStatus($"gRPC error (state_version={attemptVersion}): {rpcException.Status.Detail}", StatusKind.Error);
            }
            catch (Exception exception)
            {
                SetConnectionText("Connection error", attemptVersion);
                SetStatus($"Connection error (state_version={attemptVersion}): {FormatException(exception)}", StatusKind.Error);
            }
            finally
            {
                _streamCancellation?.Dispose();
                _streamCancellation = null;
                _channel?.Dispose();
                _channel = null;
                SetUiConnectedState(connecting: false, attemptVersion);
            }

            if (_manualDisconnectRequested)
                break;

            if (!_autoReconnectEnabled)
            {
                SetConnectionText("Disconnected", attemptVersion);
                SetStatus("Auto reconnect is disabled. Stopping retries.", StatusKind.Info);
                break;
            }

            SetConnectionText("Retrying in 5s...", attemptVersion);
            SetStatus($"Reconnecting in 5 seconds (state_version={attemptVersion})...", StatusKind.Warning);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private void Disconnect_Click(object? sender, RoutedEventArgs e)
    {
        _manualDisconnectRequested = true;
        var newVersion = Interlocked.Increment(ref _connectionStateVersion);
        DisconnectButton.IsEnabled = false;
        _streamCancellation?.Cancel();
        StatusText.Text = "Disconnected";
        SetStatus(
            $"Disconnect requested. Auto-reconnect paused. (state_version={newVersion})",
            StatusKind.Info);
    }

    private void PushEventToUi(ProfilerEvent profilerEvent)
    {
        PushGrpcEventToUi(new GrpcRawEventRow
        {
            Sequence = Interlocked.Increment(ref _grpcEventSequence),
            UnixTimeMs = profilerEvent.UnixTimeMs > 0
                ? profilerEvent.UnixTimeMs
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CommandName = profilerEvent.CommandName,
            DatabaseName = profilerEvent.DatabaseName,
            TableName = profilerEvent.CollectionName,
            DbContextId = profilerEvent.SessionId,
            ServerEndpoint = profilerEvent.HasServerEndpoint ? profilerEvent.ServerEndpoint : "-",
            ResultCount = profilerEvent.HasResultCount ? profilerEvent.ResultCount : null,
            DurationMs = profilerEvent.DurationMs,
            Status = profilerEvent.Success ? "success" : "failed",
            ShortQuery = BuildShortQuery(profilerEvent.Query),
            FullQuery = profilerEvent.Query,
            Error = profilerEvent.ErrorMessage,
            RequestId = profilerEvent.RequestId,
            OperationId = profilerEvent.HasOperationId ? profilerEvent.OperationId : string.Empty,
            TraceId = profilerEvent.TraceId,
            SpanId = profilerEvent.SpanId,
            Fingerprint = profilerEvent.HasQueryFingerprint ? profilerEvent.QueryFingerprint : string.Empty,
            WarningStatus = profilerEvent.HasIndexAdviceStatus ? profilerEvent.IndexAdviceStatus : string.Empty,
            WarningSummary = profilerEvent.HasIndexAdviceReason ? profilerEvent.IndexAdviceReason : string.Empty,
            WinningPlanSummary = profilerEvent.HasWinningPlanSummary ? profilerEvent.WinningPlanSummary : string.Empty,
            ExecutionPlanXml = profilerEvent.HasExecutionPlanXml ? profilerEvent.ExecutionPlanXml : string.Empty
        });
    }

    private void PushGrpcEventToUi(GrpcRawEventRow row)
    {
        _grpcAllEvents.Add(row);
        RebuildGrpcRows(selectEvent: row);
    }

    private void RebuildGrpcRows(GrpcRawEventRow? selectEvent = null)
    {
        var groupedStats = _grpcAllEvents
            .GroupBy(GetGroupingKey)
            .Select(group =>
            {
                var latest = group.Last();
                var warningSummary = string.Join("; ", group
                    .Select(e => e.WarningSummary)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase));

                return new
                {
                    GroupKey = group.Key,
                    QueryCount = group.Count(),
                    AvgDurationMs = group.Average(e => e.DurationMs),
                    HasFailure = group.Any(e => e.HasFailure),
                    WarningStatus = latest.WarningStatus,
                    WarningSummary = warningSummary
                };
            })
            .ToDictionary(item => item.GroupKey, StringComparer.Ordinal);

        var rows = _grpcAllEvents
            .Select(row =>
            {
                var groupKey = GetGroupingKey(row);
                var stats = groupedStats[groupKey];

                return new GrpcGridRow
                {
                    Sequence = row.Sequence,
                    GroupKey = groupKey,
                    UnixTimeMs = row.UnixTimeMs,
                    QueryCount = stats.QueryCount,
                    AvgDurationMs = stats.AvgDurationMs,
                    Status = stats.HasFailure ? "failed" : row.Status,
                    CommandName = row.CommandName,
                    DatabaseName = row.DatabaseName,
                    TableName = row.TableName,
                    DbContextId = row.DbContextId,
                    ServerEndpoint = row.ServerEndpoint,
                    ResultCount = row.ResultCount,
                    DurationMs = row.DurationMs,
                    ShortQuery = row.ShortQuery,
                    FullQuery = row.FullQuery,
                    Error = row.Error,
                    RequestId = row.RequestId,
                    OperationId = row.OperationId,
                    TraceId = row.TraceId,
                    SpanId = row.SpanId,
                    Fingerprint = row.Fingerprint,
                    WarningStatus = stats.WarningStatus,
                    WarningSummary = stats.WarningSummary,
                    WinningPlanSummary = row.WinningPlanSummary,
                    ExecutionPlanXml = row.ExecutionPlanXml
                };
            })
            .OrderByDescending(row => row.UnixTimeMs)
            .ThenByDescending(row => row.Sequence)
            .ThenByDescending(row => row.QueryCount)
            .ThenByDescending(row => row.AvgDurationMs)
            .ToList();

        _grpcRows.Clear();
        foreach (var row in rows)
            _grpcRows.Add(row);

        UpdateEfSummary();

        if (_grpcRows.Count == 0)
            return;

        var selected = selectEvent is null
            ? _grpcRows[0]
            : _grpcRows.FirstOrDefault(row => row.Sequence == selectEvent.Sequence) ?? _grpcRows[0];
        GrpcEventsGrid.SelectedItem = selected;
        GrpcEventsGrid.ScrollIntoView(selected, null);
    }

    private void SetUiConnectedState(bool connecting, long stateVersion)
    {
        void Apply()
        {
            if (Interlocked.Read(ref _connectionStateVersion) != stateVersion)
                return;

            DisconnectButton.IsEnabled = connecting;
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private void SetConnectionText(string text, long stateVersion)
    {
        void Apply()
        {
            if (Interlocked.Read(ref _connectionStateVersion) != stateVersion)
                return;

            StatusText.Text = text;
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private static string GetGroupingKey(GrpcRawEventRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.Fingerprint))
            return row.Fingerprint;

        return $"{row.CommandName}|{row.ShortQuery}";
    }
}
