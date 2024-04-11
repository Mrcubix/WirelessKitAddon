using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DesktopNotifications;
using OpenTabletDriver.External.Common.RPC;
using Splat;
using StreamJsonRpc;
using WirelessKitAddon.Lib;
using WirelessKitAddon.UX.Extensions;

namespace WirelessKitAddon.UX.ViewModels;

using static AssetLoaderExtensions;

public partial class WirelessKitViewModel : ViewModelBase, IDisposable
{
    #region Fields

    private ObservableCollection<WindowIcon> _icons = new();

    private RpcClient<IWirelessKitDaemon> _daemonClient;

    private INotificationManager? _notificationManager;

    private IWirelessKitDaemon? _daemon;

    private string _tabletName = "No tablet detected";

    private bool _pastEarlyWarning;

    private bool _pastLateWarning;

    #endregion

    #region Observable Properties

    [ObservableProperty]
    private WirelessKitInstance? _currentInstance;

    [ObservableProperty]
    private WindowIcon _currentIcon;

    [ObservableProperty]
    private string _currentToolTip = "No tablet detected";

    [ObservableProperty]
    private bool _isConnected;

    #endregion

    #region Constructors

    public WirelessKitViewModel(string[] args)
    {
        if (args.Length > 0)
            _tabletName = args[0];

        File.WriteAllText("tablet-kit.txt", _tabletName);

        _daemonClient = new RpcClient<IWirelessKitDaemon>("WirelessKitDaemon");

        var icons = LoadBitmaps(
            "Assets/battery_unknown.ico",
            "Assets/battery_0.ico",
            "Assets/battery_10.ico",
            "Assets/battery_33.ico",
            "Assets/battery_50.ico",
            "Assets/battery_75.ico",
            "Assets/battery_100.ico"
        );

        foreach (var icon in icons)
            if (icon != null)
                _icons.Add(new WindowIcon(icon));

        _currentIcon = _icons[0];

        _notificationManager = Locator.Current.GetService<INotificationManager>();

        InitializeClient();
    }

    private void InitializeClient()
    {
        _daemonClient.Connected += OnDaemonConnected;
        _daemonClient.Attached += OnClientAttached;
        _daemonClient.Disconnected += OnDaemonDisconnected;

        _ = Task.Run(ConnectRpcAsync);
    }

    #endregion

    #region Events

    public event EventHandler? CloseRequested;

    #endregion

    #region Methods

    public async Task FetchInfos()
    {
        CurrentInstance = null;

        if (_daemon != null && IsConnected)
        {
            try
            {
                CurrentInstance = await _daemon.GetInstance(_tabletName);

                if (CurrentInstance != null)
                    CurrentInstance.PropertyChanged += OnInstancePropertiesChanged;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                CurrentIcon = _icons[0];
                return;
            }

            StartMonitoringInstances();
        }
    }

    public void StartMonitoringInstances()
    {
        if (_daemon == null)
            return;

        if (CurrentInstance != null)
        {
            CurrentIcon = GetBatteryIcon(CurrentInstance.BatteryLevel);
            CurrentToolTip = BuildToolTip(CurrentInstance);
        }

        _daemon.InstanceUpdated += OnInstanceChanged;
    }

    private async Task ConnectRpcAsync()
    {
        if (_daemonClient.IsConnected)
            return;

        try
        {
            await _daemonClient.ConnectAsync();
        }
        catch (Exception e)
        {
            HandleException(e);
        }
    }

    [RelayCommand]
    public void Quit()
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    public WindowIcon GetBatteryIcon(float batteryLevel)
    {
        return batteryLevel switch
        {
            0 => _icons[1],
            > 0 and < 10 => _icons[2],
            >= 10 and < 33 => _icons[3],
            >= 33 and < 50 => _icons[4],
            >= 50 and < 75 => _icons[5],
            >= 75 => _icons[6],
            _ => _icons[0]
        };
    }

    public void HandleWarnings()
    {
        if (CurrentInstance == null)
            return;

        if (CurrentInstance.BatteryLevel <= CurrentInstance.EarlyWarningSetting && !_pastEarlyWarning)
        {
            SendNotification(CurrentInstance.Name, $"Battery under {CurrentInstance.EarlyWarningSetting}%");
            _pastEarlyWarning = true;
        }
        else if (CurrentInstance.BatteryLevel > CurrentInstance.EarlyWarningSetting)
            _pastEarlyWarning = false;

        if (CurrentInstance.BatteryLevel <= CurrentInstance.LateWarningSetting && !_pastLateWarning)
        {
            SendNotification(CurrentInstance.Name, $"Battery under {CurrentInstance.LateWarningSetting}%");
            _pastLateWarning = true;
        }
        else if (CurrentInstance.BatteryLevel > CurrentInstance.LateWarningSetting)
            _pastLateWarning = false;
    }

    private void SendNotification(string title, string message)
    {
        if (_notificationManager == null)
            return;

        var notification = new Notification()
        {
            Title = title,
            Body = message
        };

        _ = Task.Run(() => _notificationManager.ShowNotification(notification));
    }

    private static string BuildToolTip(WirelessKitInstance instance)
    {
        var toolTip = $"Tablet: {instance.Name}\nBattery: {instance.BatteryLevel}%";

        if (instance.IsCharging)
            toolTip += "\nCharging...";

        return toolTip;
    }

    #endregion

    #region Exception Handling

    private void HandleException(Exception e)
    {
        switch (e)
        {
            case RemoteRpcException re:
                Console.WriteLine($"An Error occured while attempting to connect to the RPC server: {re.Message}");
                Console.WriteLine("----------------------------------------");
                Console.WriteLine("This error could have occured due to an different version of WheelAddon being used with this Interface.");

                IsConnected = false;
                break;
            case OperationCanceledException _:
                break;
            default:
                Console.WriteLine($"An unhanded exception occured: {e.Message}");

                // write the exception to a file
                File.WriteAllText("exception.txt", e.ToString());

                Console.WriteLine("The exception has been written to exception.txt");

                break;
        }
    }

    #endregion

    #region Event Handlers

    private void OnDaemonConnected(object? sender, EventArgs e)
    {
        IsConnected = true;
    }

    private void OnClientAttached(object? sender, EventArgs e)
    {
        if (!IsConnected)
            return;

        _daemon = _daemonClient.Instance;

        _ = Task.Run(FetchInfos);
    }

    private void OnDaemonDisconnected(object? sender, EventArgs e)
    {
        IsConnected = false;
        CurrentInstance = null;

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnInstanceChanged(object? sender, WirelessKitInstance instance)
    {
        if (instance != null && instance.Name == CurrentInstance?.Name)
        {   
            CurrentInstance.BatteryLevel = instance.BatteryLevel;
            CurrentInstance.IsCharging = instance.IsCharging;
        }
    }

    private void OnInstancePropertiesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is WirelessKitInstance instance)
        {
            if (e.PropertyName == nameof(WirelessKitInstance.BatteryLevel))
            {
                CurrentIcon = GetBatteryIcon(instance.BatteryLevel);
                CurrentToolTip = BuildToolTip(instance);

                HandleWarnings();
            }

            if (e.PropertyName == nameof(WirelessKitInstance.IsCharging))
            {
                CurrentToolTip = BuildToolTip(instance);

                if (!instance.IsCharging)
                    HandleWarnings();
            }
        }
    }

    #endregion

    #region Disposal

    public void Dispose()
    {
        _daemonClient.Connected -= OnDaemonConnected;
        _daemonClient.Disconnected -= OnDaemonDisconnected;
        _daemonClient.Dispose();

        GC.SuppressFinalize(this);
    }

    #endregion
}
