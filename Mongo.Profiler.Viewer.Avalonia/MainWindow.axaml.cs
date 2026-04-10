using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Grpc.Net.Client;

namespace Mongo.Profiler.Viewer;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<GrpcGridRow> _grpcRows = [];
    private readonly List<GrpcRawEventRow> _grpcAllEvents = [];
    private readonly ObservableCollection<ProfileGridRow> _profileRows = [];
    private readonly ObservableCollection<DataDetailRow> _dataDetailsRows = [];
    private readonly ConcurrentDictionary<string, byte> _directProfileSeenKeys = new(StringComparer.Ordinal);
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