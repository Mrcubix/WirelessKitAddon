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
        if (args == null || args.Length == 0)
        {
            Console.WriteLine("No arguments provided, Tablet Name is required.");
            return;
        }

        var builder = BuildAvaloniaApp();

        // Add the notification manager to the service provider
        Locator.CurrentMutable.RegisterConstant(NotificationManager);

        builder.StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .SetupDesktopNotifications(out NotificationManager)
            .WithInterFont()
            .LogToTrace();
}
