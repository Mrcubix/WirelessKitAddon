using System;
using Avalonia;
using DesktopNotifications;
using DesktopNotifications.Avalonia;
using Splat;

namespace WirelessKitAddon.UX.Desktop;

sealed class Program
{
    private static INotificationManager? NotificationManager;

    [STAThread]
    public static void Main(string[] args) 
    {
        // Add the notification manager to the service provider
        Locator.CurrentMutable.RegisterConstant(NotificationManager);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .SetupDesktopNotifications(out NotificationManager)
            .WithInterFont()
            .LogToTrace();
}
