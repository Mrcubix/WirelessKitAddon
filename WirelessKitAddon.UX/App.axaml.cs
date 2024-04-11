using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using WirelessKitAddon.UX.ViewModels;

namespace WirelessKitAddon.UX;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            if (desktop.Args == null || desktop.Args.Length == 0)
            {
                desktop.Shutdown();
                return;
            }

            var vm = new WirelessKitViewModel(desktop.Args);

            vm.CloseRequested += delegate 
            {  
                Dispatcher.UIThread.Invoke(() => desktop.Shutdown());
            };

            DataContext = vm;
        }

        base.OnFrameworkInitializationCompleted();
    }
}