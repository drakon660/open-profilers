using Avalonia.Interactivity;
using Avalonia.Threading;
using Grpc.Core;
using Grpc.Net.Client;
using Mongo.Profiler;
using Mongo.Profiler.Grpc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Mongo.Profiler.Viewer;

public partial class MainWindow
{
    private static readonly TimeSpan DirectProfilePollInterval = TimeSpan.FromSeconds(5);
    private const int DirectProfileBatchSize = 500;

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

    private void DirectProfileModeChanged(object? sender, RoutedEventArgs e)
    {
        var directProfileEnabled = IsDirectProfileModeEnabled();
        var mode = directProfileEnabled ? "direct system.profile" : "gRPC";
        SetStatus($"Mode switched to {mode}.", StatusKind.Info);

        if (_connectLoopTask is { IsCompleted: false } || _profileLoopTask is { IsCompleted: false })
        {
            _ = RestartWithSelectedModeAsync();
            return;
        }

        // If user explicitly checks "Listen to system.profile" while idle, start listening immediately.
        if (directProfileEnabled)
            StartSelectedMode(manualRequest: true);
    }

    private async Task RestartWithSelectedModeAsync()
    {
        _manualDisconnectRequested = true;
        var newVersion = Interlocked.Increment(ref _connectionStateVersion);
        _streamCancellation?.Cancel();
        _profileCancellation?.Cancel();
        StatusText.Text = "Switching source...";
        SetStatus($"Switching listener source... (state_version={newVersion})", StatusKind.Info);

        await Task.Delay(150);

        _manualDisconnectRequested = false;
        StartSelectedMode(manualRequest: true);
    }

    private void StartSelectedMode(bool manualRequest)
    {
        if (!manualRequest && !_autoReconnectEnabled)
            return;

        if (IsDirectProfileModeEnabled())
            StartDirectProfileLoop(manualRequest);
        else
            StartReconnectLoop(manualRequest);
    }

    private bool IsDirectProfileModeEnabled()
    {
        return DirectProfileModeCheckBox.IsChecked == true;
    }

    private void StartReconnectLoop(bool manualRequest)
    {
        if (_connectLoopTask is { IsCompleted: false } || _profileLoopTask is { IsCompleted: false })
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

    private void StartDirectProfileLoop(bool manualRequest)
    {
        if (_profileLoopTask is { IsCompleted: false } || _connectLoopTask is { IsCompleted: false })
        {
            SetStatus("Already connected or connecting.", StatusKind.Warning);
            return;
        }

        _manualDisconnectRequested = false;
        if (manualRequest)
            SetStatus("Direct profile connect requested.", StatusKind.Info);
        else
            SetStatus("Auto-connect enabled. Starting direct profile loop.", StatusKind.Info);

        _profileCancellation = new CancellationTokenSource();
        _profileLoopTask = RunDirectProfileLoopAsync(_profileCancellation.Token);
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
                    ClientName = "mongo-profiler-avalonia",
                    SchemaVersion = "preview"
                }, cancellationToken: _streamCancellation.Token);

                var isConnected = false;
                SetConnectionText("Subscribed, waiting for ack/events...", attemptVersion);
                SetStatus(
                    $"Subscription opened at {host}:{port} (attempt {attempt}, state_version={attemptVersion}); waiting for server ack/events.",
                    StatusKind.Info);

                // Mark as connected only after gRPC headers arrive (actual stream handshake).
                await call.ResponseHeadersAsync;
                SetConnectionText($"Connected to {host}:{port}", attemptVersion);
                SetStatus(
                    $"Connected to gRPC at {host}:{port} (attempt {attempt}, state_version={attemptVersion})",
                    StatusKind.Info);
                attempt = 0;
                isConnected = true;

                await foreach (var profilerEvent in call.ResponseStream.ReadAllAsync(_streamCancellation.Token))
                {
                    if (!isConnected)
                    {
                        SetConnectionText($"Connected to {host}:{port}", attemptVersion);
                        SetStatus(
                            $"Connected to gRPC stream at {host}:{port} via first event (state_version={attemptVersion})",
                            StatusKind.Info);
                        isConnected = true;
                        attempt = 0;
                    }

                    await Dispatcher.UIThread.InvokeAsync(() => PushEventToUi(profilerEvent));
                }
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
                SetStatus(
                    $"gRPC error (state_version={attemptVersion}): {rpcException.Status.Detail}",
                    StatusKind.Error);
            }
            catch (Exception exception)
            {
                SetConnectionText("Connection error", attemptVersion);
                SetStatus(
                    $"Connection error (state_version={attemptVersion}): {FormatException(exception)}",
                    StatusKind.Error);
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
            SetStatus(
                $"Reconnecting in 5 seconds (state_version={attemptVersion})...",
                StatusKind.Warning);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private async Task RunDirectProfileLoopAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        BsonValue? lastTimestamp = null;

        while (!_manualDisconnectRequested && !cancellationToken.IsCancellationRequested)
        {
            attempt++;
            var attemptVersion = Interlocked.Increment(ref _connectionStateVersion);
            SetUiConnectedState(connecting: true, attemptVersion);
            SetConnectionText("Connecting (direct profile)...", attemptVersion);

            var connectionString = string.IsNullOrWhiteSpace(MongoConnectionStringBox.Text)
                ? "mongodb://localhost:27017"
                : MongoConnectionStringBox.Text.Trim();
            var databaseName = string.IsNullOrWhiteSpace(MongoDatabaseBox.Text)
                ? "profiler_samples"
                : MongoDatabaseBox.Text.Trim();

            SetStatus(
                $"Connecting to Mongo direct profile source ({databaseName}) (attempt {attempt}, state_version={attemptVersion})...",
                StatusKind.Info);

            try
            {
                var client = new MongoClient(connectionString);
                var database = client.GetDatabase(databaseName);

                // Quick first call validates connection and permissions.
                var checkpoint = await MongoSystemProfileReader.BootstrapAsync(
                    database,
                    MongoSystemProfileReader.ReaderComment,
                    cancellationToken);
                lastTimestamp = checkpoint.LastTimestamp;

                SetConnectionText($"Connected (direct profile): {databaseName}", attemptVersion);
                SetStatus(
                    $"Connected to Mongo direct profile source ({databaseName}) (attempt {attempt}, state_version={attemptVersion})",
                    StatusKind.Info);
                attempt = 0;

                while (!_manualDisconnectRequested && !cancellationToken.IsCancellationRequested)
                {
                    var keepDraining = true;
                    while (keepDraining && !_manualDisconnectRequested && !cancellationToken.IsCancellationRequested)
                    {
                        var page = await MongoSystemProfileReader.ReadNextPageAsync(
                            database,
                            new MongoSystemProfileCheckpoint
                            {
                                LastTimestamp = lastTimestamp
                            },
                            DirectProfileBatchSize,
                            cancellationToken);

                        lastTimestamp = page.Checkpoint.LastTimestamp;

                        if (page.RawDocumentCount == 0)
                            break;

                        var rowsToAppend = new List<ProfileGridRow>();
                        foreach (var entry in page.Entries)
                        {
                            if (!_directProfileSeenKeys.TryAdd(entry.EventKey, 0))
                                continue;

                            rowsToAppend.Add(ToProfileGridRow(entry));
                        }

                        if (rowsToAppend.Count > 0)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                foreach (var row in rowsToAppend)
                                    PushProfileEventToUi(row);
                            });
                        }

                        // If we filled the batch, there may be more pending documents; keep draining.
                        keepDraining = page.RawDocumentCount >= DirectProfileBatchSize;
                    }

                    await Task.Delay(DirectProfilePollInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                SetConnectionText("Disconnected", attemptVersion);
                SetStatus($"Disconnected (state_version={attemptVersion}).", StatusKind.Info);
            }
            catch (MongoCommandException commandException) when (commandException.Code == 26)
            {
                SetConnectionText("system.profile unavailable", attemptVersion);
                SetStatus(
                    $"system.profile not found for database '{databaseName}'. Enable profiling first. (state_version={attemptVersion})",
                    StatusKind.Warning);
            }
            catch (Exception exception)
            {
                SetConnectionText("Connection error", attemptVersion);
                SetStatus(
                    $"Direct profile error (state_version={attemptVersion}): {FormatException(exception)}",
                    StatusKind.Error);
            }
            finally
            {
                SetUiConnectedState(connecting: false, attemptVersion);
            }

            if (_manualDisconnectRequested || cancellationToken.IsCancellationRequested)
                break;

            if (!_autoReconnectEnabled)
            {
                SetConnectionText("Disconnected", attemptVersion);
                SetStatus("Auto reconnect is disabled. Stopping retries.", StatusKind.Info);
                break;
            }

            SetConnectionText("Retrying in 5s...", attemptVersion);
            SetStatus(
                $"Reconnecting in 5 seconds (state_version={attemptVersion})...",
                StatusKind.Warning);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private void Disconnect_Click(object? sender, RoutedEventArgs e)
    {
        _manualDisconnectRequested = true;
        var newVersion = Interlocked.Increment(ref _connectionStateVersion);
        DisconnectButton.IsEnabled = false;
        _streamCancellation?.Cancel();
        _profileCancellation?.Cancel();
        StatusText.Text = "Disconnected";
        SetStatus(
            $"Disconnect requested. Auto-reconnect paused. (state_version={newVersion})",
            StatusKind.Info);
    }

    private void PushEventToUi(ProfilerEvent profilerEvent)
    {
        PushGrpcEventToUi(new GrpcRawEventRow
        {
            UnixTimeMs = profilerEvent.UnixTimeMs > 0
                ? profilerEvent.UnixTimeMs
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CommandName = profilerEvent.CommandName,
            SessionId = profilerEvent.SessionId,
            ServerEndpoint = profilerEvent.HasServerEndpoint ? profilerEvent.ServerEndpoint : "-",
            ResultCount = profilerEvent.HasResultCount ? profilerEvent.ResultCount : null,
            DurationMs = profilerEvent.DurationMs,
            Status = profilerEvent.Success ? "success" : "failed",
            ShortQuery = BuildShortQuery(profilerEvent.Query),
            FullQuery = profilerEvent.Query,
            Error = profilerEvent.ErrorMessage,
            OperationId = profilerEvent.HasOperationId ? profilerEvent.OperationId : string.Empty,
            ErrorCode = profilerEvent.HasErrorCode ? profilerEvent.ErrorCode : null,
            ErrorCodeName = profilerEvent.HasErrorCodeName ? profilerEvent.ErrorCodeName : string.Empty,
            Fingerprint = profilerEvent.HasQueryFingerprint ? profilerEvent.QueryFingerprint : string.Empty,
            WinningPlanSummary = profilerEvent.HasWinningPlanSummary ? profilerEvent.WinningPlanSummary : string.Empty,
            ExecutionPlanXml = profilerEvent.HasExecutionPlanXml ? profilerEvent.ExecutionPlanXml : string.Empty,
            OriginalCommand = profilerEvent.HasOriginalCommand ? profilerEvent.OriginalCommand : string.Empty
        });
    }

    private void PushGrpcEventToUi(GrpcRawEventRow row)
    {
        RemoveSampleRows();
        _grpcAllEvents.Add(row);
        RebuildGrpcRows(selectEvent: row);
    }

    private static ProfileGridRow ToProfileGridRow(MongoSystemProfileEntry entry)
    {
        return new ProfileGridRow
        {
            TimestampUtc = entry.TimestampUtc,
            TsRaw = entry.TsRaw,
            AppName = entry.AppName,
            Client = entry.Client,
            CommandDocument = entry.CommandDocument,
            DocsExamined = entry.DocsExamined,
            NReturned = entry.NReturned,
            Op = entry.Op,
            CommandName = entry.CommandName,
            ServerEndpoint = entry.ServerEndpoint,
            DurationMs = entry.DurationMs,
            Status = entry.Success ? "success" : "failed",
            Error = entry.ErrorMessage
        };
    }

    private void RebuildGrpcRows(GrpcRawEventRow? selectEvent = null)
    {
        var grouped = _grpcAllEvents
            .GroupBy(GetGroupingKey)
            .Select(group =>
            {
                var latest = group
                    .OrderByDescending(e => e.UnixTimeMs)
                    .First();
                var count = group.Count();
                var avgDurationMs = group.Average(e => e.DurationMs);
                var hasFailure = group.Any(e => string.Equals(e.Status, "failed", StringComparison.OrdinalIgnoreCase));

                return new GrpcGridRow
                {
                    GroupKey = group.Key,
                    UnixTimeMs = latest.UnixTimeMs,
                    QueryCount = count,
                    AvgDurationMs = avgDurationMs,
                    Status = hasFailure ? "failed" : "success",
                    CommandName = latest.CommandName,
                    SessionId = latest.SessionId,
                    ServerEndpoint = latest.ServerEndpoint,
                    ResultCount = latest.ResultCount,
                    DurationMs = latest.DurationMs,
                    ShortQuery = latest.ShortQuery,
                    FullQuery = latest.FullQuery,
                    OperationId = latest.OperationId,
                    ErrorCode = latest.ErrorCode,
                    ErrorCodeName = latest.ErrorCodeName,
                    Fingerprint = latest.Fingerprint,
                    WinningPlanSummary = latest.WinningPlanSummary,
                    ExecutionPlanXml = latest.ExecutionPlanXml,
                    OriginalCommand = latest.OriginalCommand
                };
            })
            .OrderByDescending(row => row.UnixTimeMs)
            .ThenByDescending(row => row.QueryCount)
            .ToList();

        _grpcRows.Clear();
        foreach (var row in grouped)
            _grpcRows.Add(row);

        if (_grpcRows.Count == 0)
            return;

        var selected = selectEvent is null
            ? _grpcRows[0]
            : _grpcRows.FirstOrDefault(row => row.GroupKey == GetGroupingKey(selectEvent)) ?? _grpcRows[0];
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

    private void PushProfileEventToUi(ProfileGridRow row)
    {
        RemoveSampleRows();
        var insertIndex = 0;
        while (insertIndex < _profileRows.Count && CompareProfileRowsDesc(_profileRows[insertIndex], row) <= 0)
            insertIndex++;

        _profileRows.Insert(insertIndex, row);
        ProfileEventsGrid.SelectedItem = row;
        ProfileEventsGrid.ScrollIntoView(row, null);
    }

    private static int CompareProfileRowsDesc(ProfileGridRow current, ProfileGridRow incoming)
    {
        var currentTicks = current.TimestampUtc?.UtcTicks ?? long.MinValue;
        var incomingTicks = incoming.TimestampUtc?.UtcTicks ?? long.MinValue;
        return incomingTicks.CompareTo(currentTicks);
    }
}
